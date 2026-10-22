using LongLeap.Achievements;
using LongLeap.Localization;
using TMPro;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// Строка списка ачивок в <see cref="AchievementsWindow"/>: название и отметка
    /// «выполнена / не выполнена».
    ///
    /// Названий в определениях ачивок нет — только id. Поэтому название ищется в
    /// локализации по ключу из <see cref="_titleKeyFormat"/>, а нет такого ключа —
    /// собирается из id: «first_ride» → «First ride» (так же его пишет уведомление
    /// об открытой ачивке).
    /// </summary>
    public sealed class AchievementItemView : MonoBehaviour
    {
        [Header("Title")]
        [SerializeField] private TMP_Text _titleLabel;

        [Tooltip("Ключ локализации названия: {0} — id ачивки. Ключа нет в таблице — " +
                 "название собирается из id.")]
        [SerializeField] private string _titleKeyFormat = "achievement_{0}_title";

        [Header("Status")]
        [Tooltip("Включён, когда ачивка выполнена (галочка, подсветка).")]
        [SerializeField] private GameObject _completedRoot;

        [Tooltip("Включён, когда ачивка ещё не выполнена (замок, серый фон).")]
        [SerializeField] private GameObject _notCompletedRoot;

        [Tooltip("Подпись статуса. Необязательна: хватает и рутов выше.")]
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

            // Нет перевода — Loc отдаёт сам ключ: значит, названия в таблице нет.
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
