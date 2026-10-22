using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Что показать в одной строке награды: иконка эффекта и что он даст.
    /// Собирает окно рюкзака из каталога товаров — см. <see cref="UserBackPackWindow"/>.
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
    /// Одна строка награды в карточке <see cref="BackPackItemView"/>: иконка и
    /// подпись («+$100», «+1 Shield x2»). Живёт шаблоном внутри карточки —
    /// карточка размножает его по наградам предмета.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackPackRewardView : MonoBehaviour
    {
        [Tooltip("Иконка эффекта. Не задана в конфиге — прячется.")]
        [SerializeField] private Image _icon;

        [Tooltip("Что даст награда: описание эффекта из конфига.")]
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
