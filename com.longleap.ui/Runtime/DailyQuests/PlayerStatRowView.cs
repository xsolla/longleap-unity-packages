using TMPro;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// Строка верхней части <see cref="DailyQuestPanel"/>: подпись слева, значение
    /// справа («TOTAL SCORE … 2,450,000»). Что в ней показывать, решает панель.
    /// </summary>
    public sealed class PlayerStatRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private TMP_Text _value;

        [Tooltip("Включается только в строке валюты (иконка денег и т.п.). " +
                 "Необязателен.")]
        [SerializeField] private GameObject _currencyRoot;

        /// <summary>Строка показывает валюту — включить <see cref="_currencyRoot"/>.</summary>
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
