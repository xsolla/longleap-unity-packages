using TMPro;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// A row in the top part of <see cref="DailyQuestPanel"/>: label on the left, value
    /// on the right ("TOTAL SCORE … 2,450,000"). The panel decides what it shows.
    /// </summary>
    public sealed class PlayerStatRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private TMP_Text _value;

        [Tooltip("Enabled only in the currency row (money icon, etc.). " +
                 "Optional.")]
        [SerializeField] private GameObject _currencyRoot;

        /// <summary>The row shows currency — enable <see cref="_currencyRoot"/>.</summary>
        public void SetCurrency(bool isCurrency)
        {
            if (_currencyRoot != null)
                _currencyRoot.SetActive(isCurrency);
        }

        public void SetLabel(string label)
        {
            if (_label != null)
                _label.text = label;
        }

        public void SetValue(string value)
        {
            if (_value != null)
                _value.text = value;
        }
    }
}
