using System.Threading.Tasks;
using LongLeap.Achievements;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Ачивки — страница главного меню: вертикальный список, в строке название и
    /// выполнена ли ачивка (<see cref="AchievementItemView"/>). Открывается кнопкой
    /// Achievements в главном меню игры, «назад» возвращает в меню
    /// (см. <see cref="MenuPageWindow"/>).
    ///
    /// Систему ачивок окну приносит загрузка игры: сборка UI про
    /// контейнер сервисов не знает. Не принесла — список пуст и показан
    /// <see cref="_emptyRoot"/>.
    ///
    /// Список пересобирается на каждом открытии: ачивки открываются в заезде, пока
    /// окно закрыто, и следить за ними на лету незачем.
    ///
    /// Разметка в префабе — обычный вертикальный ScrollRect:
    /// <code>
    /// AchievementsWindow
    /// └── Root
    ///     └── Scroll View   (ScrollRect: Vertical, без Horizontal)
    ///         └── Viewport  (RectMask2D)
    ///             └── Content  (VerticalLayoutGroup + ContentSizeFitter: Vertical = Preferred)
    /// </code>
    /// Content — это <see cref="_itemsParent"/>, строки создаются в нём.
    /// </summary>
    public sealed class AchievementsWindow : MenuPageWindow
    {
        [Header("List")]
        [Tooltip("Скролл списка — на открытии прокручивается в начало. Необязателен.")]
        [SerializeField] private ScrollRect _scroll;

        [Tooltip("Content скролла: сюда создаются строки ачивок.")]
        [SerializeField] private Transform _itemsParent;

        [SerializeField] private AchievementItemView _itemPrefab;

        [Tooltip("Показывается, когда показать нечего: системы ачивок нет или список " +
                 "пуст. Необязателен.")]
        [SerializeField] private GameObject _emptyRoot;

        private IAchievementSystem _achievements;

        /// <summary>
        /// Отдать окну систему ачивок. Зовёт загрузка игры, когда
        /// система поднята.
        /// </summary>
        public void Bind(IAchievementSystem achievements)
        {
            _achievements = achievements;

            if (IsOpen)
                Rebuild();
        }

        public override async Task OpenAsync()
        {
            await base.OpenAsync();

            Rebuild();

            // Открыли заново — с начала списка, а не там, где бросили в прошлый раз.
            if (_scroll != null)
                _scroll.verticalNormalizedPosition = 1f;
        }

        private void Rebuild()
        {
            ClearItems();

            if (_itemPrefab == null || _itemsParent == null)
            {
                Debug.LogError("[Achievements] у окна не назначены Item Prefab или Items Parent.", this);
                ShowEmpty(true);
                return;
            }

            var all = _achievements?.GetAll();
            var shown = 0;

            if (all != null)
            {
                foreach (var status in all)
                {
                    // Скрытая ачивка не видна, пока её не открыли: иначе секрет
                    // выдал бы сам список.
                    if (status?.Definition == null ||
                        (status.Definition.IsHidden && !status.IsUnlocked))
                        continue;

                    var item = Instantiate(_itemPrefab, _itemsParent);
                    item.Set(status);
                    shown++;
                }
            }

            ShowEmpty(shown == 0);
        }

        private void ClearItems()
        {
            if (_itemsParent == null)
                return;

            for (int i = _itemsParent.childCount - 1; i >= 0; i--)
                Destroy(_itemsParent.GetChild(i).gameObject);
        }

        private void ShowEmpty(bool visible)
        {
            if (_emptyRoot != null && _emptyRoot.activeSelf != visible)
                _emptyRoot.SetActive(visible);
        }
    }
}
