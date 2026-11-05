using LongLeap.Achievements;
using LongLeap.Localization;
using TMPro;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// A row of the achievement list in <see cref="AchievementsWindow"/>: the title and a
    /// "completed / not completed" mark.
    ///
    /// Achievement definitions have no titles — only ids. So the title is looked up in
    /// localization by the key from <see cref="_titleKeyFormat"/>; if there's no such key,
    /// it's built from the id: "first_ride" → "First ride" (the same way the
    /// achievement-unlocked notification writes it).
    /// </summary>
    public sealed class AchievementItemView : MonoBehaviour
    {
        [Header("Title")]
        [SerializeField] private TMP_Text _titleLabel;

        [Tooltip("Localization key for the title: {0} is the achievement id. If the key isn't in the table, " +
                 "the title is built from the id.")]
        [SerializeField] private string _titleKeyFormat = "achievement_{0}_title";

        [Header("Status")]
        [Tooltip("Enabled when the achievement is completed (checkmark, highlight).")]
        [SerializeField] private GameObject _completedRoot;

        [Tooltip("Enabled when the achievement isn't completed yet (lock, grey background).")]
        [SerializeField] private GameObject _notCompletedRoot;

        [Tooltip("Status label. Optional: the roots above are enough.")]
        [SerializeField] private TMP_Text _statusLabel;

        [SerializeField] private string _completedText = "COMPLETED";
        [SerializeField] private string _notCompletedText = "NOT COMPLETED";

        private AchievementStatus _status;

        private void OnEnable()
        {
            Loc.OnLanguageChanged += ApplyTitle;
        }

        private void OnDisable()
        {
            Loc.OnLanguageChanged -= ApplyTitle;
        }

        public void Set(AchievementStatus status)
        {
            _status = status;

            ApplyTitle();
            ApplyState();
        }

        private void ApplyState()
        {
            var completed = _status != null && _status.IsUnlocked;

            if (_completedRoot != null)
                _completedRoot.SetActive(completed);

            if (_notCompletedRoot != null)
                _notCompletedRoot.SetActive(!completed);

            if (_statusLabel != null)
                _statusLabel.text = completed ? _completedText : _notCompletedText;
        }

        private void ApplyTitle()
        {
            if (_titleLabel == null || _status?.Definition == null)
                return;

            _titleLabel.text = ResolveTitle(_status.Definition.Id.Value);
        }

        private string ResolveTitle(string id)
        {
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

            var text = id.Replace('_', ' ').Trim();
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
