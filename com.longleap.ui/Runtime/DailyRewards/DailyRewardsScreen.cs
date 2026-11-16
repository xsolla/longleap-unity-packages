using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.DailyRewards;
using LongLeap.Tools.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Daily rewards window — a main menu page: opened by a menu button
    /// or automatically, once per launch, if today's reward has not been claimed yet
    /// (the game's main menu decides). A claimable card grants the reward when pressed,
    /// and Continue returns to the menu after the result has been shown.
    ///
    /// The rewards service is handed to the window by the game loading (via
    /// <see cref="IWindowService.OnCreated{TWindow}"/> — for every created instance:
    /// the window is unloaded on close). If it does not, the window shows loading and the
    /// rewards button in the menu is hidden.
    /// </summary>
    public sealed class DailyRewardsScreen : MenuPageWindow
    {
        [Header("Days")]
        [SerializeField] private Transform _daysParent;
        [SerializeField] private DailyRewardDayView _dayPrefab;

        [Header("Loading")]
        [Tooltip("\"Loading\" root inside the window: shown when there is nothing to draw " +
            "the calendar with — the rewards service did not come up or its store did not answer " +
            "and 0 days arrived. Hidden as soon as the days arrive. Not assigned — the window just " +
            "stays empty.")]
        [SerializeField] private GameObject _loadingRoot;

        [Tooltip("How many seconds to wait for the days under the loading root. On timeout the root " +
            "stays on screen (there is nothing to show anyway) and an error goes " +
            "to the log; the player can leave with the back button. 0 — wait without a limit.")]
        [Min(0f)]
        [SerializeField] private float _daysLoadTimeoutSec = 10f;

        [Header("Continue")]
        [SerializeField] private Button _continueButton;

        private IDailyRewardsService _service;
        private IDailyRewardsPresentation _presentation;
        private IUiSounds _sounds;
        private Func<string, Sprite> _rewardIcon;
        private bool _revealing;
        private bool _leaving;

        // Cards live between redraws: the reward reveal plays on an already created card.
        private readonly List<DailyRewardDayView> _dayViews = new List<DailyRewardDayView>();

        private Coroutine _revealRoutine;

        // Timeout for waiting for the days under the loading root. The days themselves arrive via
        // IDailyRewardsService.Changed — the window is rebuilt in OnServiceChanged.
        private Coroutine _waitDaysRoutine;

        /// <summary>
        /// Hand the rewards service to the window. Called by the game loading once the service is up.
        /// </summary>
        /// <param name="sounds">"Claimed" and "locked" sounds. Optional.</param>
        /// <param name="rewardIcon">Reward picture by its key. Optional: without it
        /// the card shows only a label.</param>
        public void Bind(IDailyRewardsService service, IUiSounds sounds = null,
            Func<string, Sprite> rewardIcon = null)
        {
            if (_service != null)
                _service.Changed -= OnServiceChanged;

            _service = service;

            if (_service != null)
                _service.Changed += OnServiceChanged;

            _sounds = sounds;
            _rewardIcon = rewardIcon;
            _presentation?.Bind(service, sounds, rewardIcon);

            if (IsOpen)
                Rebuild();
        }

        // =========================================================
        // WINDOW
        // =========================================================

        public override async Task OpenAsync()
        {
            // Cancel a reveal left over from an interrupted previous showing.
            StopRevealRoutine();
            _revealing = false;
            _leaving = false;

            await base.OpenAsync();

            Rebuild();
        }

        public override async Task CloseAsync()
        {
            _presentation?.OnClosed();
            // Waiting for the days does not survive closing: otherwise it would sit silently until
            // the timeout in the menu and leave the root on for the next showing.
            StopWaitingForDays();
            StopRevealRoutine();
            ShowLoading(false);

            await base.CloseAsync();
        }

        // =========================================================
        // UNITY
        // =========================================================

        protected override void Awake()
        {
            base.Awake();
            _presentation = GetComponentInChildren<IDailyRewardsPresentation>(true);

            var backdrop = Root.GetComponent<Image>();
            if (backdrop != null)
            {
                backdrop.sprite = null;
                backdrop.color = RuntimeUi.Backdrop;
            }

            // The loading root may live outside Root — hide it separately, otherwise
            // it would hang on screen from the first frame, before any showing.
            ShowLoading(false);

            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(OnContinueClicked);
            }
        }

        /// <summary>While the window is open, watch for the daily reset — the new day becomes claimable.</summary>
        private void Update()
        {
            if (IsOpen)
                _service?.Tick();
        }

        private void OnDisable()
        {
            // Unity stops coroutines when the host is disabled; discard their handles.
            _revealRoutine = null;
            _waitDaysRoutine = null;
            _revealing = false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_service != null)
                _service.Changed -= OnServiceChanged;

            if (_continueButton != null)
                _continueButton.onClick.RemoveListener(OnContinueClicked);
        }

        // =========================================================
        // CALENDAR
        // =========================================================

        private void Rebuild()
        {
            if (_presentation != null)
            {
                _presentation.Refresh();
                return;
            }
            ClearDays();
            StopWaitingForDays();

            // The window is not set up — there is nothing to draw the calendar with. Show the loading
            // root anyway: the player reads an empty window as broken, and the setup
            // problem is visible in the log.
            if (_dayPrefab == null || _daysParent == null)
            {
                Debug.LogError("[DailyRewards] the window has no Day Prefab or Days Parent assigned.", this);
                ShowLoading(true);
                UpdateContinueButton();
                return;
            }

            // No service at all — the calendar has nowhere to come from, nothing to wait for.
            if (_service == null)
            {
                Debug.LogError("[DailyRewards] the window was opened without a rewards service — nothing to show.", this);
                ShowLoading(true);
                UpdateContinueButton();
                return;
            }

            var calendar = _service.GetCalendar();

            // No days: the service is not ready (e.g. the store did not answer) and returns
            // an empty calendar. The days may still arrive: stay on the loading root
            // and wait for them.
            if (calendar == null || calendar.Length == 0)
            {
                ShowLoading(true);
                UpdateContinueButton();

                // The window may still be inactive (Bind before showing) — a coroutine cannot
                // start on an inactive object; OpenAsync will start it.
                if (isActiveAndEnabled)
                    _waitDaysRoutine = StartCoroutine(WaitForDays());

                return;
            }

            ShowLoading(false);

            for (int i = 0; i < calendar.Length; i++)
            {
                var view = Instantiate(_dayPrefab, _daysParent);
                view.RewardIcon = _rewardIcon;
                view.Set(calendar[i]);
                view.Clicked += OnCardClicked;
                view.LockedClicked += OnLockedCardClicked;
                _dayViews.Add(view);
            }

            UpdateContinueButton();
        }

        // The service changed something: a day was claimed, the day rolled over, progress was reset.
        // While a claimed reward is being revealed, leave the cards alone so as not to cut
        // the animation short; RevealReward re-reads the calendar itself when the reveal ends.
        // A custom presentation (_presentation) subscribes to the service itself.
        private void OnServiceChanged()
        {
            if (this == null || !IsOpen || _presentation != null || _revealing)
                return;

            if (_dayViews.Count == 0)
                Rebuild();
            else
                RefreshDays();
        }

        // Stay under the loading root until the days appear. When they arrive, Changed
        // rebuilds the window and stops this wait; if they do not arrive in time — log it.
        private IEnumerator WaitForDays()
        {
            if (_daysLoadTimeoutSec <= 0f)
                yield break;

            // Realtime: the window is opened from the menu, where time may be stopped
            // (Time.timeScale = 0) — a regular wait would never tick.
            yield return new WaitForSecondsRealtime(_daysLoadTimeoutSec);

            _waitDaysRoutine = null;

            // Do NOT hide the loading root: there is still nothing to show, and an empty window
            // is worse than "loading". The player can still leave with the back button.
            Debug.LogError("[DailyRewards] calendar days did not arrive within " +
                $"{_daysLoadTimeoutSec:0.#} s — the window stays on loading.", this);
        }

        private void StopWaitingForDays()
        {
            if (_waitDaysRoutine == null)
                return;

            StopCoroutine(_waitDaysRoutine);
            _waitDaysRoutine = null;
        }

        private void ShowLoading(bool visible)
        {
            if (_loadingRoot != null && _loadingRoot.activeSelf != visible)
                _loadingRoot.SetActive(visible);
        }

        // Re-read day states into the existing cards: a claimed day
        // gets its "claimed" mark. If the calendar suddenly has a different length,
        // rebuild the list from scratch.
        private void RefreshDays()
        {
            var calendar = _service?.GetCalendar();
            if (calendar == null || calendar.Length != _dayViews.Count)
            {
                Rebuild();
                return;
            }

            for (int i = 0; i < _dayViews.Count; i++)
            {
                if (_dayViews[i] != null)
                    _dayViews[i].Set(calendar[i]);
            }
        }

        private void ClearDays()
        {
            foreach (var view in _dayViews)
                if (view != null)
                {
                    view.Clicked -= OnCardClicked;
                    view.LockedClicked -= OnLockedCardClicked;
                }
            _dayViews.Clear();

            if (_daysParent == null)
                return;

            for (int i = _daysParent.childCount - 1; i >= 0; i--)
                Destroy(_daysParent.GetChild(i).gameObject);
        }

        private void UpdateContinueButton()
        {
            if (_continueButton == null)
                return;

            _continueButton.interactable = !_revealing && !_leaving;
        }

        // =========================================================
        // CLAIM
        // =========================================================

        private void OnContinueClicked()
        {
            if (_revealing || _leaving) return;
            _leaving = true;
            UpdateContinueButton();
            Forget(ContinueToMenuAsync());
        }

        private async Task ContinueToMenuAsync()
        {
            try { await BackAsync(); }
            finally
            {
                _leaving = false;
                if (this != null) UpdateContinueButton();
            }
        }

        private void OnCardClicked(DailyRewardDayView view)
        {
            if (_service == null || _revealing || _leaving || !view.IsClaimable) return;
            _revealing = true;
            UpdateContinueButton();
            Forget(ClaimAsync(view));
        }

        private async Task ClaimAsync(DailyRewardDayView view)
        {
            bool claimed;
            try { claimed = await _service.ClaimAsync(); }
            catch (System.Exception exception)
            {
                _revealing = false;
                Debug.LogException(exception, this);
                if (this != null) UpdateContinueButton();
                return;
            }

            // The window may have been closed or unloaded while the save was in flight.
            if (this == null || !isActiveAndEnabled || view == null)
            {
                _revealing = false;
                return;
            }

            if (!claimed)
            {
                _revealing = false;
                Debug.LogWarning("[DailyRewards] ClaimAsync returned false — no reward granted.", this);
                RefreshDays();
                UpdateContinueButton();
                return;
            }

            _sounds?.Play(UiSound.RewardClaimed);
            _revealRoutine = StartCoroutine(RevealReward(view));
        }

        private void OnLockedCardClicked(DailyRewardDayView view)
        {
            if (!_revealing && !_leaving)
                _sounds?.Play(UiSound.Denied);
        }

        private IEnumerator RevealReward(DailyRewardDayView view)
        {
            yield return view.RevealClaimed();
            _revealRoutine = null;
            _revealing = false;
            // Changed was skipped during the reveal: the next day must become "locked until tomorrow".
            RefreshDays();
            UpdateContinueButton();
        }

        private void StopRevealRoutine()
        {
            if (_revealRoutine == null)
                return;

            StopCoroutine(_revealRoutine);
            _revealRoutine = null;
        }
    }
}
