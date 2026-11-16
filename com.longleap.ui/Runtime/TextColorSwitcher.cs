using TMPro;
using UnityEngine;

namespace LongLeap.UI
{
    /// Switches text color between two states. The methods can be hooked to UI events (Button, Toggle, EventTrigger).
    public class TextColorSwitcher : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] TMP_Text target;                       // empty = taken from this same object

        [Header("Colors")]
        [SerializeField] Color normalColor = Color.white;
        [SerializeField] Color highlightColor = Color.yellow;

        void Awake()
        {
            if (target == null) target = GetComponent<TMP_Text>();
        }

        /// Normal color.
        public void SetNormal() => Apply(normalColor);

        /// Highlight color.
        public void SetHighlight() => Apply(highlightColor);

        void Apply(Color color)
        {
            if (target == null) return;
            target.color = color;
        }
    }
}
