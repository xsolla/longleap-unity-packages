using System.Collections.Generic;
using LongLeap.Backpack;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>What can currently be done with a backpack item — see <see cref="BackPackItemView.SetState"/>.</summary>
    public enum BackPackItemState
    {
        /// <summary>Can be selected for the run.</summary>
        Available,

        /// <summary>Selected: activates at the start of the run. Pressing again deselects it.</summary>
        Selected,

        /// <summary>
        /// Cannot be selected: the game doesn't know its reward, its product doesn't fit
        /// the moment the backpack was opened for, the maximum of that product is already
        /// selected, or the ongoing run won't accept it (full health, shield already active).
        /// </summary>
        Unavailable
    }

    /// <summary>
    /// A single card in <see cref="UserBackPackWindow"/>: an item from the player's
    /// server-side backpack — image, title, description, rewards and an "activate" button.
    ///
    /// Rewards are a column of rows (<see cref="BackPackRewardView"/>): the effect icon
    /// and what it grants, e.g. a coin and "+$100". The server sends only reward
    /// keys, while icons and labels are known to the game (product catalog), so the
    /// window builds the rows and passes them here (<see cref="SetRewards"/>).
    ///
    /// The button doesn't spend the item right away; it selects it for the run (pressing
    /// again deselects it): the window can spend the item and grant its product only
    /// once the run has started — see <see cref="UserBackPackWindow.ActivateSelectedAsync"/>.
    /// The window decides the card's state (<see cref="SetState"/>); the card
    /// only renders it.
    ///
    /// The image arrives from the server as a URL and is downloaded after the card
    /// is shown (<see cref="SetIcon"/>) — until then the icon is hidden so that a
    /// white square doesn't hang in its place.
    ///
    /// Lives in the project as a TEMPLATE (usually a separate prefab) — the window clones
    /// it for the backpack contents, so keep the object disabled:
    /// an enabled template would stay on screen as an extra card.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackPackItemView : MonoBehaviour
    {
        [Tooltip("Item image.")]
        [SerializeField] private Image _icon;

        [Tooltip("Item title.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("Item description.")]
        [SerializeField] private TMP_Text _description;

        [Header("Rewards")]

        [Tooltip("Header above the reward list. Optional. Hidden when there are no rewards.")]
        [SerializeField] private TMP_Text _rewards;

        [Tooltip("Reward header text.")]
        [SerializeField] private string _rewardsPrefix = "REWARD: ";

        [Tooltip("Reward row template: icon and label. Lives inside the card — keep it " +
                 "disabled: an enabled one stays as an extra empty row.")]
        [SerializeField] private BackPackRewardView _rewardTemplate;

        [Tooltip("Where to put reward rows (object with a VerticalLayoutGroup). Empty — the template's parent.")]
        [SerializeField] private Transform _rewardsRoot;

        [Header("Activation")]

        [Tooltip("'Activate' button: selects the item for the run; pressing again deselects it.")]
        [SerializeField] private Button _activateButton;

        [Tooltip("Button label. Optional — without it the button doesn't change its text.")]
        [SerializeField] private TMP_Text _activateLabel;

        [Tooltip("Button label while the item is not selected.")]
        [SerializeField] private string _activateText = "ACTIVATE";

        [Tooltip("Button label when the item is selected for the run.")]
        [SerializeField] private string _selectedText = "ACTIVE";

        [Tooltip("Button label when the item cannot be selected.")]
        [SerializeField] private string _unavailableText = "UNAVAILABLE";

        [Header("Background")]

        [Tooltip("Card background: its sprite changes when the item is selected for the run. Optional.")]
        [SerializeField] private Image _background;

        [Tooltip("Background while the item is not selected.")]
        [SerializeField] private Sprite _normalFrame;

        [Tooltip("Background when the item is selected for the run.")]
        [SerializeField] private Sprite _selectedFrame;

        /// <summary>Created reward rows — reused on every <see cref="SetRewards"/>.</summary>
        private readonly List<BackPackRewardView> _rewardViews = new List<BackPackRewardView>();

        private bool _warnedNoRewardTemplate;

        /// <summary>The item this card shows — set in <see cref="Show"/>.</summary>
        public BackPackRewardItem Item { get; private set; }

        /// <summary>The "activate" button — the window attaches its handler to it.</summary>
        public Button ActivateButton => _activateButton;

        public void Show(BackPackRewardItem item)
        {
            Item = item;

            // The item may have no name — then at least the sku, otherwise the card has no title.
            if (_title != null)
                _title.text = string.IsNullOrEmpty(item.Name) ? item.Sku : item.Name;

            if (_description != null)
                _description.text = item.Description ?? "";

            SetIcon(null);
            SetRewards(null);
            SetState(BackPackItemState.Available);
        }

        /// <summary>
        /// Rewards in a column, one row each. Empty — hide the header too rather
        /// than leaving it above an empty space. Extra rows from the previous
        /// call are disabled, not destroyed: the window calls this on every reload.
        /// </summary>
        public void SetRewards(IReadOnlyList<BackPackRewardEntry> rewards)
        {
            var count = rewards?.Count ?? 0;

            if (_rewards != null)
            {
                _rewards.gameObject.SetActive(count > 0);
                _rewards.text = _rewardsPrefix;
            }

            var template = ResolveRewardTemplate(count > 0);
            if (template == null) return;

            var parent = _rewardsRoot != null ? _rewardsRoot : template.transform.parent;

            while (_rewardViews.Count < count)
                _rewardViews.Add(Instantiate(template, parent));

            for (var i = 0; i < _rewardViews.Count; i++)
            {
                var visible = i < count;
                _rewardViews[i].gameObject.SetActive(visible);

                if (visible)
                    _rewardViews[i].Show(rewards[i]);
            }
        }

        public void SetIcon(Sprite icon)
        {
            if (_icon == null) return;

            _icon.sprite = icon;
            _icon.enabled = icon != null;
        }

        public void SetState(BackPackItemState state)
        {
            if (_activateButton != null)
                _activateButton.interactable = state != BackPackItemState.Unavailable;

            // Don't assign an empty sprite: a field forgotten in the inspector must not wipe the background.
            var frame = state == BackPackItemState.Selected ? _selectedFrame : _normalFrame;

            if (_background != null && frame != null)
                _background.sprite = frame;

            if (_activateLabel == null) return;

            switch (state)
            {
                case BackPackItemState.Selected:
                    _activateLabel.text = _selectedText;
                    break;

                case BackPackItemState.Unavailable:
                    _activateLabel.text = _unavailableText;
                    break;

                default:
                    _activateLabel.text = _activateText;
                    break;
            }
        }

        /// <summary>
        /// The reward row template, disabled: if enabled it would remain as an extra
        /// empty row. No template — complain once, and only when there is
        /// something to show.
        /// </summary>
        private BackPackRewardView ResolveRewardTemplate(bool needed)
        {
            if (_rewardTemplate == null)
            {
                if (needed && !_warnedNoRewardTemplate)
                {
                    _warnedNoRewardTemplate = true;
                    Debug.LogError(
                        "[BackPack] the card has no reward row template (Reward Template) — " +
                        "rewards won't be shown.", this);
                }

                return null;
            }

            if (_rewardTemplate.gameObject.activeSelf)
                _rewardTemplate.gameObject.SetActive(false);

            return _rewardTemplate;
        }
    }
}
