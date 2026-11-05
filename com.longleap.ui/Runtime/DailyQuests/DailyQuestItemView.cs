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
    /// A row in the bottom part of <see cref="DailyQuestPanel"/>: the status first (empty
    /// frame / checkmark), followed by the quest title.
    ///
    /// Completed quests show a checkmark and ACTIVATE badge. The row requests
    /// <see cref="IDailyQuestService.ClaimAsync"/> only when clicked.
    ///
    /// The quest JSON has no titles — only ids and objectives. So the title is looked up in
    /// localization (<see cref="_titleKeyFormat"/>); if there's no key, it's built from the
    /// objectives via <see cref="_objectiveTitles"/>: "grind" 50 → "Do 50 grinds".
    /// An unknown stat falls back to the id, as with achievements: "grind_50" → "Grind 50".
    /// </summary>
    public sealed class DailyQuestItemView : MonoBehaviour
    {
        [Serializable]
        private struct ObjectiveTitle
        {
            [Tooltip("Objective stat path from daily_quests.json (statPath).")]
            public string StatPath;

            [Tooltip("Objective title: {0} is the target amount (25K, 1.5M).")]
            public string Format;
        }

        [Header("Status")]
        [SerializeField] private Image _statusIcon;
        [SerializeField] private Sprite _incompleteSprite;
        [SerializeField] private Sprite _completeSprite;

        [Header("Title")]
        [SerializeField] private TMP_Text _titleLabel;

        [Tooltip("Localization key for the title: {0} is the quest id. If the key isn't in the table, " +
                 "the title is built from the objectives.")]
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

        [Tooltip("Separator used to join objectives of a multi-objective quest.")]
        [SerializeField] private string _objectiveSeparator = " & ";

        [Tooltip("ACTIVATE badge for a completed quest.")]
        [SerializeField] private Sprite _activateSprite;

        [Header("Colors")]
        [SerializeField] private Color _inProgressColor = Color.white;
        [SerializeField] private Color _claimableColor = new Color(0f, 1f, 1f, 1f);
        [SerializeField] private Color _claimedColor = new Color(0.1f, 1f, 0.1f, 1f);

        [Header("Claim")]
        [Tooltip("Clicking the row claims the reward. Optional: without it " +
                 "the quest is view-only.")]
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

        /// <summary>A completed quest was clicked — time to claim the reward.</summary>
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
        // TITLE
        // =========================================================

        private string ResolveTitle(QuestDefinition definition)
        {
            var id = definition?.Id.Value;
            if (string.IsNullOrWhiteSpace(id))
                return string.Empty;

            // No translation — Loc returns the key itself, meaning the title isn't in the table.
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

        /// <summary>Title built from objectives. Fails if any objective has no format.</summary>
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

        /// <summary>Shorten large targets: 25000 → 25K, 1500000 → 1.5M.</summary>
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
