using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// What to show in a single reward row: the effect icon and what it grants.
    /// Built by the backpack window from the product catalog — see <see cref="UserBackPackWindow"/>.
    /// </summary>
    public readonly struct BackPackRewardEntry
    {
        public readonly Sprite Icon;
        public readonly string Text;

        public BackPackRewardEntry(Sprite icon, string text)
        {
            Icon = icon;
            Text = text;
        }
    }

    /// <summary>
    /// A single reward row in a <see cref="BackPackItemView"/> card: icon and
    /// label ("+$100", "+1 Shield x2"). Lives as a template inside the card —
    /// the card clones it for each of the item's rewards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackPackRewardView : MonoBehaviour
    {
        [Tooltip("Effect icon. Hidden if not set in the config.")]
        [SerializeField] private Image _icon;

        [Tooltip("What the reward grants: the effect description from the config.")]
        [SerializeField] private TMP_Text _label;

        public void Show(BackPackRewardEntry entry)
        {
            if (_icon != null)
            {
                _icon.sprite = entry.Icon;
                _icon.enabled = entry.Icon != null;
            }

            if (_label != null)
                _label.text = entry.Text ?? "";
        }
    }
}
