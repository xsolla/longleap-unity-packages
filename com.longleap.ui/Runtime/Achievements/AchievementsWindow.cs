using System.Threading.Tasks;
using LongLeap.Achievements;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Achievements — a main menu page: a vertical list, each row showing the title and
    /// whether the achievement is completed (<see cref="AchievementItemView"/>). Opened by the
    /// Achievements button in the game's main menu; "back" returns to the menu
    /// (see <see cref="MenuPageWindow"/>).
    ///
    /// The achievement system is passed to the window by the game's loading sequence: the UI
    /// assembly doesn't know about the service container. If it isn't provided, the list is
    /// empty and <see cref="_emptyRoot"/> is shown.
    ///
    /// The list is rebuilt on every open: achievements unlock during a run while the
    /// window is closed, so there's no need to track them live.
    ///
    /// The prefab layout is a regular vertical ScrollRect:
    /// <code>
    /// AchievementsWindow
    /// └── Root
    ///     └── Scroll View   (ScrollRect: Vertical, no Horizontal)
    ///         └── Viewport  (RectMask2D)
    ///             └── Content  (VerticalLayoutGroup + ContentSizeFitter: Vertical = Preferred)
    /// </code>
    /// Content is <see cref="_itemsParent"/>; rows are created inside it.
    /// </summary>
    public sealed class AchievementsWindow : MenuPageWindow
    {
        [Header("List")]
        [Tooltip("List scroll — scrolled back to the top on open. Optional.")]
        [SerializeField] private ScrollRect _scroll;

        [Tooltip("Scroll Content: achievement rows are created here.")]
        [SerializeField] private Transform _itemsParent;

        [SerializeField] private AchievementItemView _itemPrefab;

        [Tooltip("Shown when there's nothing to show: no achievement system or the list " +
                 "is empty. Optional.")]
        [SerializeField] private GameObject _emptyRoot;

        private IAchievementSystem _achievements;

        /// <summary>
        /// Give the window the achievement system. Called by the game's loading sequence once
        /// the system is up.
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

            // Reopened — start from the top of the list, not where it was left last time.
            if (_scroll != null)
                _scroll.verticalNormalizedPosition = 1f;
        }

        private void Rebuild()
        {
            ClearItems();

            if (_itemPrefab == null || _itemsParent == null)
            {
                Debug.LogError("[Achievements] window has no Item Prefab or Items Parent assigned.", this);
                ShowEmpty(true);
                return;
            }

            var all = _achievements?.GetAll();
            var shown = 0;

            if (all != null)
            {
                foreach (var status in all)
                {
                    // A hidden achievement isn't shown until unlocked: otherwise the list
                    // itself would give the secret away.
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
