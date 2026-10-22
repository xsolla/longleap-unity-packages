using System;
using System.Collections;
using System.Text;
using LongLeap.DailyRewards;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using LongLeap.Localization;

namespace LongLeap.UI
{
    public class DailyRewardDayView : MonoBehaviour, IPointerClickHandler, ISubmitHandler, IOwnClickSound
    {
        /// <summary>
        /// Картинка награды по её ключу (RewardEntry.Key). Задаёт окно наград — из того,
        /// что отдала игра. Нет картинки — null.
        /// </summary>
        public Func<string, Sprite> RewardIcon { get; set; }

        [Header("States")]
        [Tooltip("Рут открытой карточки: день доступен для получения или уже забран.")]
        [SerializeField] private GameObject _openRoot;
        [Tooltip("Рут закрытой карточки: день ещё недоступен.")]
        [SerializeField] private GameObject _closedRoot;
        [Tooltip("Объект-отметка о том, что награда уже забрана.")]
        [SerializeField] private GameObject _claimedMark;

        [Header("References")]
        [SerializeField] private TextMeshProUGUI _dayLabel;
        [SerializeField] private TextMeshProUGUI _statusLabel;
        [SerializeField] private Image _rewardIcon;

        [Header("Card artwork")]
        [SerializeField] private Texture2D _closedTexture;
        [SerializeField] private Texture2D _readyTexture;
        [SerializeField] private Texture2D _glowTexture;
        [SerializeField] private Texture2D _openTexture;

        public event Action<DailyRewardDayView> Clicked;
        public event Action<DailyRewardDayView> LockedClicked;
        public bool IsClaimable => _day?.State == CalendarDayState.Claimable && !_revealing;
        private bool IsLocked => !_revealing && _day != null &&
            (_day.State == CalendarDayState.Locked || _day.State == CalendarDayState.Future);

        // Disabled Buttons still receive pointer events on their raycast target.
        // Handle locked attempts separately without making the reward claimable.
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && IsLocked)
                LockedClicked?.Invoke(this);
        }

        public void OnSubmit(BaseEventData data)
        {
            if (IsLocked) LockedClicked?.Invoke(this);
        }
        private GameObject _readyRoot;
        private RawImage _glow;
        private RectTransform _readyRect;
        private CanvasGroup _readyGroup;
        private CanvasGroup _openGroup;
        private Button _button;
        private bool _revealing;

        private CalendarDay _day;

        private void Awake()
        {
            BuildCardArt();

            // Without card artwork the prefab layout stays as is, but the card still
            // has to be clickable to claim the day.
            EnsureButton();
            ApplyState();
        }

        private void EnsureButton()
        {
            if (_button != null) return;

            _button = GetComponent<Button>();
            if (_button == null)
            {
                var hitArea = GetComponent<Graphic>();
                if (hitArea == null)
                {
                    var image = gameObject.AddComponent<Image>();
                    image.color = Color.clear;
                    hitArea = image;
                }

                _button = gameObject.AddComponent<Button>();
                _button.targetGraphic = hitArea;
                _button.transition = Selectable.Transition.None;
            }

            _button.onClick.AddListener(() => { if (IsClaimable) Clicked?.Invoke(this); });
        }

        private void BuildCardArt()
        {
            if (_closedTexture == null || _readyTexture == null ||
                _glowTexture == null || _openTexture == null) return;

            // Retain the reward labels and icon from the existing layout.
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            _closedRoot = CreateArt("LockedCard", _closedTexture, new Vector2(305f, 389f)).gameObject;
            _glow = CreateArt("ReadyGlow", _glowTexture, new Vector2(305f, 389f));
            var ready = CreateArt("ReadyCard", _readyTexture, new Vector2(240f, 325f));
            _readyRoot = ready.gameObject;
            _readyRect = ready.rectTransform;
            _readyGroup = _readyRoot.AddComponent<CanvasGroup>();
            var open = CreateArt("OpenedCard", _openTexture, new Vector2(234f, 308f));
            _openRoot = open.gameObject;
            _openGroup = _openRoot.AddComponent<CanvasGroup>();

            MoveContent(_dayLabel, new Vector2(0f, 90f), new Vector2(180f, 50f));
            MoveContent(_statusLabel, new Vector2(0f, -62f), new Vector2(190f, 42f));
            MoveContent(_rewardIcon, new Vector2(0f, 10f), new Vector2(110f, 90f));
            if (_dayLabel != null)
            {
                _dayLabel.fontSize = 36f;
                _dayLabel.alignment = TextAlignmentOptions.Center;
                _dayLabel.color = Color.white;
            }
            if (_statusLabel != null)
            {
                _statusLabel.fontSize = 26f;
                _statusLabel.alignment = TextAlignmentOptions.Center;
                _statusLabel.color = new Color(0.08f, 0.95f, 1f);
            }
            if (_rewardIcon != null) _rewardIcon.preserveAspect = true;
            var mark = SettingsUi.CreateText("Claimed", open.transform,
                _dayLabel != null ? _dayLabel.font : null, "CLAIMED", 20,
                new Color(0.08f, 0.95f, 1f), TextAlignmentOptions.Center);
            mark.rectTransform.sizeDelta = new Vector2(180f, 30f);
            mark.rectTransform.anchoredPosition = new Vector2(0f, -112f);
            _claimedMark = mark.gameObject;

            var hitArea = gameObject.AddComponent<Image>();
            hitArea.color = Color.clear;
            _button = gameObject.AddComponent<Button>();
            _button.targetGraphic = hitArea;
            _button.transition = Selectable.Transition.None;
            _button.onClick.AddListener(() => { if (IsClaimable) Clicked?.Invoke(this); });
            ApplyState();
        }

        private RawImage CreateArt(string name, Texture texture, Vector2 size)
        {
            var rect = SettingsUi.CreateRect(name, transform);
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            return image;
        }

        private void MoveContent(Graphic graphic, Vector2 position, Vector2 size)
        {
            if (graphic == null) return;
            var rect = graphic.rectTransform;
            rect.SetParent(_openRoot.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            graphic.raycastTarget = false;
            graphic.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!IsClaimable || _readyRect == null || !_readyRoot.activeSelf) return;
            _readyRect.anchoredPosition = new Vector2(0f, 8f + Mathf.Sin(Time.unscaledTime * 2.6f) * 5f);
        }

        public IEnumerator RevealClaimed()
        {
            // Commit the displayed result too: the service may start a new cycle on the last day.
            _day = new CalendarDay { Day = _day.Day, State = CalendarDayState.Claimed };
            _revealing = true;
            if (_button != null) _button.interactable = false;
            if (_readyRect == null)
            {
                _revealing = false;
                ApplyState();
                yield break;
            }

            Vector2 start = _readyRect.anchoredPosition;
            for (float elapsed = 0f; elapsed < 0.24f; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / 0.24f);
                float eased = t * t;
                _readyRect.anchoredPosition = start + Vector2.up * (45f * eased);
                _readyRect.localScale = new Vector3(Mathf.Lerp(1f, 0.06f, eased), 1f + 0.12f * t, 1f);
                _readyRect.localRotation = Quaternion.Euler(0f, 0f, -8f * t);
                _readyGroup.alpha = 1f - eased;
                _glow.rectTransform.localScale = Vector3.one * (1f + 0.12f * t);
                yield return null;
            }

            _readyRoot.SetActive(false);
            _openRoot.SetActive(true);
            ApplyLocalization();
            _claimedMark.SetActive(true);
            var openRect = (RectTransform)_openRoot.transform;
            for (float elapsed = 0f; elapsed < 0.36f; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / 0.36f);
                float easeOut = 1f - Mathf.Pow(1f - t, 3f);
                openRect.localScale = Vector3.one * (0.82f + 0.18f * easeOut + Mathf.Sin(t * Mathf.PI) * 0.06f);
                _openGroup.alpha = easeOut;
                _glow.color = new Color(1f, 1f, 1f, 1f - t);
                yield return null;
            }
            _revealing = false;
            ApplyState();
        }

        void OnEnable()
        {
            Loc.OnLanguageChanged += ApplyLocalization;
            ApplyState();
        }

        void OnDisable()
        {
            Loc.OnLanguageChanged -= ApplyLocalization;
            _revealing = false;
        }

        public void Set(CalendarDay day)
        {
            _day = day;
            ApplyState();
            ApplyLocalization();
        }

        void ApplyState()
        {
            if (_day == null)
                return;

            bool isOpen = _day.State == CalendarDayState.Claimed ||
                (_readyRoot == null && _day.State == CalendarDayState.Claimable);

            if (_openRoot != null)
                _openRoot.SetActive(isOpen);

            if (_closedRoot != null)
                _closedRoot.SetActive(!isOpen && !IsClaimable);

            if (_readyRoot != null)
            {
                _readyRoot.SetActive(IsClaimable);
                _readyRect.localScale = Vector3.one;
                _readyRect.localRotation = Quaternion.identity;
                _readyGroup.alpha = 1f;
                _glow.gameObject.SetActive(IsClaimable);
                _glow.color = Color.white;
                _glow.rectTransform.localScale = Vector3.one;
                _openRoot.transform.localScale = Vector3.one;
                _openGroup.alpha = 1f;
                _button.interactable = IsClaimable;
            }
            else if (_button != null)
            {
                _button.interactable = IsClaimable;
            }

            if (_claimedMark != null)
                _claimedMark.SetActive(_day.State == CalendarDayState.Claimed);

            if (_rewardIcon != null)
            {
                var sprite = ResolveIcon();
                _rewardIcon.sprite = sprite;
                _rewardIcon.enabled = sprite != null;
            }
        }

        void ApplyLocalization()
        {
            if (_day == null)
                return;

            if (_dayLabel != null)
            {
                const string key = "daily_rewards_day_format";
                string format = Loc.Resolve(key);
                if (string.IsNullOrWhiteSpace(format) || format == key) format = "DAY {0}";
                _dayLabel.text = string.Format(format, _day.Day.DayIndex + 1);
            }

            if (_statusLabel != null)
                _statusLabel.text = BuildRewardText();
        }

        /// <summary>Текст награды дня: строка «+50» на каждую запись награды.</summary>
        string BuildRewardText()
        {
            var rewards = _day.Day?.Rewards;
            if (rewards == null || rewards.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var reward in rewards)
            {
                if (reward == null)
                    continue;

                if (sb.Length > 0)
                    sb.Append('\n');

                sb.Append('+').Append(reward.Amount);

                // Валюту и так видно по картинке — подпись нужна, только если её нет.
                if (FindIcon(reward.Key.Value) == null)
                    sb.Append(' ').Append(reward.Key.Value);
            }

            return sb.ToString();
        }

        Sprite FindIcon(string key) => RewardIcon != null ? RewardIcon(key) : null;

        // Картинку берём по ключу первой награды, у которой она вообще есть
        // (см. RewardIcon): на карточке место под одну иконку.
        Sprite ResolveIcon()
        {
            var rewards = _day.Day?.Rewards;
            if (rewards == null)
                return null;

            foreach (var reward in rewards)
            {
                if (reward == null)
                    continue;

                var icon = FindIcon(reward.Key.Value);
                if (icon != null)
                    return icon;
            }

            return null;
        }
    }
}
