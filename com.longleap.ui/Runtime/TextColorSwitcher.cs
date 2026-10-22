using TMPro;
using UnityEngine;

namespace LongLeap.UI
{
    /// Переключение цвета текста между двумя состояниями. Методы можно вешать на события UI (Button, Toggle, EventTrigger).
    public class TextColorSwitcher : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] TMP_Text target;                       // пусто = берём с этого же объекта

        [Header("Colors")]
        [SerializeField] Color normalColor = Color.white;
        [SerializeField] Color highlightColor = Color.yellow;

        void Awake()
        {
            if (target == null) target = GetComponent<TMP_Text>();
        }

        /// Основной цвет.
        public void SetNormal() => Apply(normalColor);

        /// Акцентный цвет.
        public void SetHighlight() => Apply(highlightColor);

        void Apply(Color color)
        {
            if (target == null) return;
            target.color = color;
        }
    }
}
