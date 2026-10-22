using System;
using System.Text;
using LongLeap.DailyQuests;
using LongLeap.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Строка нижней части <see cref="DailyQuestPanel"/>: сначала статус (пустая
    /// рамка / галочка), за ним название квеста.
    ///
    /// Completed quests show a checkmark and ACTIVATE badge. The row requests
    /// <see cref="IDailyQuestService.ClaimAsync"/> only when clicked.
    ///
    /// Названий в JSON квестов нет — только id и цели. Поэтому название ищется в
    /// локализации (<see cref="_titleKeyFormat"/>), а нет ключа — собирается из
    /// целей по <see cref="_objectiveTitles"/>: «grind» 50 → «Do 50 grinds».
    /// Незнакомый стат — из id, как у ачивок: «grind_50» → «Grind 50».
    /// </summary>
    public sealed class DailyQuestItemView : MonoBehaviour
    {
        [Serializable]
        private struct ObjectiveTitle
        {
            [Tooltip("Путь стата цели из daily_quests.json (statPath).")]
            public string StatPath;

            [Tooltip("Название цели: {0} — сколько набрать (25K, 1.5M).")]
            public string Format;
        }

        [Header("Status")]
        [SerializeField] private Image _statusIcon;
        [SerializeField] private Sprite _incompleteSprite;
        [SerializeField] private Sprite _completeSprite;

        [Header("Title")]
        [SerializeField] private TMP_Text _titleLabel;

        [Tooltip("Ключ локализации названия: {0} — id квеста. Ключа нет в таблице — " +
                 "название собирается из целей.")]
        [SerializeField] private string _titleKeyFormat = "quest_{0}_title";

        [SerializeField] private ObjectiveTitle[] _objectiveTitles =
        {
            new ObjectiveTitle { StatPath = "grind", Format = "Do {0} grinds" },
            new ObjectiveTitle { StatPath = "manual", Format = "Do {0} manuals" },
            new ObjectiveTitle { StatPath = "ollie", Format = "Do {0} ollies" },
            new ObjectiveTitle { StatPath = "trick", Format = "Land {0} tricks" },
            new ObjectiveTitle { StatPath = "session", Format = "Ride {0} sessions" },
            new ObjectiveTitle { StatPath = "score_total", Format = "Score {0} points" }
        };

        [Tooltip("Чем склеиваются цели квеста из нескольких целей.")]
        [SerializeField] private string _objectiveSeparator = " & ";

        [Tooltip("Плашка ACTIVATE для выполненного квеста.")]
        [SerializeField] private Sprite _activateSprite;

        [Header("Colors")]
        [SerializeField] private Color _inProgressColor = Color.white;
        [SerializeField] private Color _claimableColor = new Color(0f, 1f, 1f, 1f);
        [SerializeField] private Color _claimedColor = new Color(0.1f, 1f, 0.1f, 1f);

        [Header("Claim")]
        [Tooltip("Нажатие на строку забирает награду. Необязательна: без неё " +
                 "квест можно только посмотреть.")]
        [SerializeField] private Button _button;

        private QuestStatus _status;
        private Image _activateBadge;
        private GameObject _pendingCheck;
        private Vector2 _titleOriginalOffsetMax;
        private Color _statusOriginalColor;
        private bool _graphicsInitialized;
        private Vector2 _titleOriginalOffsetMin;
        private Vector2 _iconOriginalPosition;
        private Vector2 _iconOriginalSize;

        /// <summary>Нажали на выполненный квест — пора забирать награду.</summary>
        public event Action<QuestStatus> ClaimRequested;

        private void Awake()
        {
            EnsureGraphicsInitialized();
            if (_button != null)
                _button.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnClick);
        }

        private void OnEnable()
        {
            Loc.OnLanguageChanged += Apply;
        }

        private void OnDisable()
        {
            Loc.OnLanguageChanged -= Apply;
        }

        public void Set(QuestStatus status)
        {
            _status = status;

            Apply();
        }

        private void Apply()
        {
            EnsureGraphicsInitialized();
            var state = _status?.State ?? QuestState.InProgress;

            if (_statusIcon != null)
                _statusIcon.sprite = state == QuestState.Claimed ? _completeSprite : _incompleteSprite;

            if (_titleLabel != null)
            {
                var title = ResolveTitle(_status?.Definition);

                _titleLabel.text = title;
                _titleLabel.rectTransform.offsetMax = _titleOriginalOffsetMax;
                _titleLabel.rectTransform.offsetMin = _titleOriginalOffsetMin;
                _titleLabel.textWrappingMode = TextWrappingModes.Normal;

                _titleLabel.color = state switch
                {
                    QuestState.Completed => _claimableColor,
                    QuestState.Claimed => _claimedColor,
                    _ => _inProgressColor
                };
            }

            if (_button != null)
                _button.interactable = _status != null && _status.CanClaim;
            if (_activateBadge != null) _activateBadge.gameObject.SetActive(state == QuestState.Completed);
            if (_pendingCheck != null) _pendingCheck.SetActive(state == QuestState.Completed);
            if (_statusIcon != null)
            {
                _statusIcon.gameObject.SetActive(true);
                _statusIcon.enabled = true;
                _statusIcon.color = _statusOriginalColor;
                _statusIcon.rectTransform.anchoredPosition = _iconOriginalPosition;
                _statusIcon.rectTransform.sizeDelta = _iconOriginalSize;
            }
        }

        private void EnsureGraphicsInitialized()
        {
            if (_graphicsInitialized) return;
            _graphicsInitialized = true;
            if (_titleLabel != null)
            {
                _titleOriginalOffsetMax = _titleLabel.rectTransform.offsetMax;
                _titleOriginalOffsetMin = _titleLabel.rectTransform.offsetMin;
            }
            if (_statusIcon != null)
            {
                _statusOriginalColor = Color.white;
                _iconOriginalPosition = _statusIcon.rectTransform.anchoredPosition;
                _iconOriginalSize = _statusIcon.rectTransform.sizeDelta;
            }
            CreateActivationGraphics();
        }

        private void CreateActivationGraphics()
        {
            if (_activateSprite != null)
            {
                var obj = new GameObject("ActivateBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                obj.layer = gameObject.layer;
                obj.transform.SetParent(transform, false);
                _activateBadge = obj.GetComponent<Image>();
                _activateBadge.sprite = _activateSprite;
                _activateBadge.raycastTarget = true;
                var badgeButton = obj.AddComponent<Button>();
                badgeButton.targetGraphic = _activateBadge;
                badgeButton.transition = Selectable.Transition.None;
                badgeButton.onClick.AddListener(OnClick);
                var rect = _activateBadge.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = new Vector2(-10f, 0f);
                rect.sizeDelta = new Vector2(133f, 55.5f);
                obj.SetActive(false);
            }
            if (_statusIcon != null)
            {
                _pendingCheck = new GameObject("PendingActivationCheck", typeof(RectTransform));
                _pendingCheck.layer = gameObject.layer;
                var rect = (RectTransform)_pendingCheck.transform;
                rect.SetParent(_statusIcon.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                CreateCheckStroke(new Vector2(-7f, -2f), 13f, -45f);
                CreateCheckStroke(new Vector2(4f, 2f), 22f, 45f);
                _pendingCheck.SetActive(false);
            }
        }

        private void CreateCheckStroke(Vector2 position, float length, float angle)
        {
            var obj = new GameObject("CheckStroke", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.layer = gameObject.layer;
            var rect = (RectTransform)obj.transform;
            rect.SetParent(_pendingCheck.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(length, 5f);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            var image = obj.GetComponent<Image>();
            image.color = new Color32(0, 255, 255, 255);
            image.raycastTarget = false;
        }

        private void OnClick()
        {
            if (_status != null && _status.CanClaim)
                ClaimRequested?.Invoke(_status);
        }

        // =========================================================
        // НАЗВАНИЕ
        // =========================================================

        private string ResolveTitle(QuestDefinition definition)
        {
            var id = definition?.Id.Value;
            if (string.IsNullOrWhiteSpace(id))
                return string.Empty;

            // Нет перевода — Loc отдаёт сам ключ: значит, названия в таблице нет.
            if (!string.IsNullOrEmpty(_titleKeyFormat))
            {
                var key = string.Format(_titleKeyFormat, id);
                var localized = Loc.Resolve(key);

                if (!string.IsNullOrEmpty(localized) && localized != key)
                    return localized;
            }

            if (TryBuildFromObjectives(definition.Objectives, out var built))
                return built;

            var text = id.Replace('_', ' ').Trim();
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>Название из целей. Хоть одна цель без формата — не выходит.</summary>
        private bool TryBuildFromObjectives(QuestObjective[] objectives, out string title)
        {
            title = null;

            if (objectives == null || objectives.Length == 0)
                return false;

            var builder = new StringBuilder();

            foreach (var objective in objectives)
            {
                if (objective == null || !TryFindFormat(objective.StatPath, out var format))
                    return false;

                if (builder.Length > 0)
                    builder.Append(_objectiveSeparator);

                builder.AppendFormat(format, FormatAmount(objective.StatTarget));
            }

            title = builder.ToString();
            return true;
        }

        private bool TryFindFormat(string statPath, out string format)
        {
            format = statPath switch
            {
                "combo_10_kickflip" => "Land 10 Kickflips in one combo",
                "combo_10_heelflip" => "Land 10 Heelflips in one combo",
                "combo_10_fs_shove_it" => "Land 10 FS Shove-Its in one combo",
                "combo_10_bs_shove_it" => "Land 10 BS Shove-Its in one combo",
                _ => null
            };
            if (format != null) return true;
            foreach (var entry in _objectiveTitles)
            {
                if (!string.IsNullOrEmpty(entry.Format) &&
                    string.Equals(entry.StatPath, statPath, StringComparison.OrdinalIgnoreCase))
                {
                    format = entry.Format;
                    return true;
                }
            }

            format = null;
            return false;
        }

        /// <summary>Крупные цели короче: 25000 → 25K, 1500000 → 1.5M.</summary>
        private static string FormatAmount(int amount)
        {
            if (amount >= 1_000_000)
                return (amount / 1_000_000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";

            if (amount >= 10_000)
                return (amount / 1_000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K";

            return amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
