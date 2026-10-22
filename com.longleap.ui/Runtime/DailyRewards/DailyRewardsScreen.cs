using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.DailyRewards;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Окно ежедневных наград — страница главного меню: открывается кнопкой в меню
    /// или само, один раз за запуск, если сегодняшняя награда ещё не забрана
    /// (решает главное меню игры). Доступная карта получает награду при нажатии,
    /// а Continue возвращает в меню после просмотра результата.
    ///
    /// Сервис наград окну приносит загрузка игры (через
    /// <see cref="IWindowService.OnCreated{TWindow}"/> — на каждый созданный экземпляр:
    /// окно выгружается при закрытии). Не принесла — окно покажет загрузку, а кнопка
    /// наград в меню спрячется.
    /// </summary>
    public sealed class DailyRewardsScreen : MenuPageWindow
    {
        [Header("Days")]
        [SerializeField] private Transform _daysParent;
        [SerializeField] private DailyRewardDayView _dayPrefab;

        [Header("Loading")]
        [Tooltip("Рут «загрузка» внутри окна: включается, когда календарь рисовать " +
            "нечем — сервис наград не поднялся либо его хранилище не ответило и дней " +
            "пришло 0. Гаснет, как только дни приехали. Не назначен — окно просто " +
            "останется пустым.")]
        [SerializeField] private GameObject _loadingRoot;

        [Tooltip("Сколько секунд ждать дни под рутом загрузки. Не дождались — рут " +
            "остаётся на экране (показывать всё равно нечего), а в лог уходит " +
            "ошибка; выйти игрок может кнопкой «назад». 0 — ждать без ограничения.")]
        [Min(0f)]
        [SerializeField] private float _daysLoadTimeoutSec = 10f;

        [Header("Continue")]
        [SerializeField] private Button _continueButton;

        private IDailyRewardsService _service;
        private IUiSounds _sounds;
        private Func<string, Sprite> _rewardIcon;
        private bool _revealing;
        private bool _leaving;

        // Reveal the existing view without rebuilding it or losing the final cycle's reward.
        private readonly List<DailyRewardDayView> _dayViews = new List<DailyRewardDayView>();

        private Coroutine _revealRoutine;

        // Ожидание дней под рутом загрузки. Опрашиваем сервис, а не подписываемся:
        // события «я доинициализировался» у него нет, а календарь он отдаёт пустым
        // ровно до тех пор, пока не приехало состояние.
        private Coroutine _waitDaysRoutine;

        // Как часто спрашивать календарь, пока висит загрузка. Раз в кадр незачем:
        // GetCalendar каждый раз собирает массив заново.
        private const float DaysPollIntervalSec = 0.25f;

        /// <summary>
        /// Отдать окну сервис наград. Зовёт загрузка, когда сервис поднят.
        /// </summary>
        /// <param name="sounds">Звуки получения и «закрыто». Необязательны.</param>
        /// <param name="rewardIcon">Картинка награды по её ключу. Необязательна: без неё
        /// на карточке только подпись.</param>
        public void Bind(IDailyRewardsService service, IUiSounds sounds = null,
            Func<string, Sprite> rewardIcon = null)
        {
            _service = service;
            _sounds = sounds;
            _rewardIcon = rewardIcon;

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
            // Ожидание дней закрытое окно не переживает: иначе оно домолчало бы до
            // таймаута уже из меню и оставило рут включённым к следующему показу.
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

            var backdrop = Root.GetComponent<Image>();
            if (backdrop != null)
            {
                backdrop.sprite = null;
                backdrop.color = SettingsUi.Backdrop;
            }

            // Рут загрузки может лежать и вне Root — гасим его отдельно, иначе
            // он висел бы на экране с первого кадра, до всякого показа.
            ShowLoading(false);

            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(OnContinueClicked);
            }
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

            if (_continueButton != null)
                _continueButton.onClick.RemoveListener(OnContinueClicked);
        }

        // =========================================================
        // КАЛЕНДАРЬ
        // =========================================================

        private void Rebuild()
        {
            ClearDays();
            StopWaitingForDays();

            // Окно не размечено — рисовать календарь нечем и не станет. Рут загрузки
            // всё равно поднимаем: пустое окно игрок читает как поломку, а причину
            // разметки видно в логе.
            if (_dayPrefab == null || _daysParent == null)
            {
                Debug.LogError("[DailyRewards] у окна не назначены Day Prefab или Days Parent.", this);
                ShowLoading(true);
                UpdateContinueButton();
                return;
            }

            // Сервиса нет вовсе — календарю приехать неоткуда, ждать нечего.
            if (_service == null)
            {
                Debug.LogError("[DailyRewards] окно открыли без сервиса наград — показывать нечего.", this);
                ShowLoading(true);
                UpdateContinueButton();
                return;
            }

            var calendar = _service.GetCalendar();

            // Дней нет: хранилище не ответило и состояние пустое — на пустом
            // состоянии сервис закрывает календарь целиком. Дни при этом могут
            // приехать: остаёмся на загрузке и ждём их.
            if (calendar == null || calendar.Length == 0)
            {
                ShowLoading(true);
                UpdateContinueButton();

                // Окно может быть ещё выключено (Bind до показа) — корутину на
                // неактивном объекте не запустить, её запустит OpenAsync.
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

        // Досидеть под рутом загрузки до появления дней. Как только календарь ответил
        // непустым — пересобираем окно целиком: Rebuild и карточки создаст, и рут
        // погасит.
        private IEnumerator WaitForDays()
        {
            float elapsed = 0f;

            while (true)
            {
                // Realtime: окно открывают из меню, где время может стоять
                // (Time.timeScale = 0) — на обычном ожидании корутина не тикала бы.
                yield return new WaitForSecondsRealtime(DaysPollIntervalSec);

                elapsed += DaysPollIntervalSec;

                var calendar = _service?.GetCalendar();
                if (calendar != null && calendar.Length > 0)
                {
                    // Обнуляем ДО Rebuild: он снимает это же ожидание, и без обнуления
                    // корутина остановила бы саму себя посреди работы.
                    _waitDaysRoutine = null;
                    Rebuild();
                    yield break;
                }

                if (_daysLoadTimeoutSec > 0f && elapsed >= _daysLoadTimeoutSec)
                {
                    // Рут загрузки НЕ гасим: показать по-прежнему нечего, а пустое окно
                    // хуже, чем «грузится». Выход у игрока есть — кнопка «назад».
                    Debug.LogError("[DailyRewards] дни календаря не приехали за " +
                        $"{_daysLoadTimeoutSec:0.#} с — окно осталось на загрузке.", this);

                    _waitDaysRoutine = null;
                    yield break;
                }
            }
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

        // Перечитать состояния дней уже созданными карточками: у забранного дня
        // включится отметка «получено». Если календарь внезапно другой длины —
        // честно пересобираем список.
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
            bool claimed;
            try { claimed = _service.Claim(); }
            catch (System.Exception exception)
            {
                _revealing = false;
                UpdateContinueButton();
                Debug.LogException(exception, this);
                return;
            }
            if (!claimed)
            {
                _revealing = false;
                Debug.LogWarning("[DailyRewards] Claim вернул false — награда не выдана.", this);
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
