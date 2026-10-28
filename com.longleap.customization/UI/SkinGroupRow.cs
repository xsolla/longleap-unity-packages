using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Customization
{
    /// <summary>
    /// Одна строка внутри открытой категории: подпись группы (Hair, Hat…) и её
    /// варианты в ряд. Категория обычно состоит из нескольких таких групп —
    /// голова показывает отдельной строкой Hair, отдельной Hat и так далее.
    ///
    /// Живёт в проекте ШАБЛОНОМ: <see cref="CustomizationPanel"/> размножает
    /// его по группам открытой категории (см. <c>Category_Root</c> —
    /// <see cref="UnityEngine.UI.VerticalLayoutGroup"/>, строки друг под другом) и в
    /// каждую копию кладёт варианты (<see cref="SkinVariantView"/>) её группы —
    /// в <see cref="ItemsRoot"/> (там <see cref="UnityEngine.UI.HorizontalLayoutGroup"/>,
    /// варианты в ряд).
    ///
    /// Список тянут пальцем (ScrollRect на Items) или кнопками Prev/Next — они
    /// не меняют выбор, только прокручивают, чтобы стали видны скрытые маской
    /// варианты.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkinGroupRow : MonoBehaviour
    {
        [Tooltip("Подпись группы: Hair, Hat… Необязательна.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("Сколько символов подписи показывать: строка узкая, длинное название " +
                 "группы в неё не влезет. Обрезается посимвольно, без «…». 0 — не обрезать.")]
        [SerializeField] private int _titleMaxLength = 5;

        [Tooltip("Куда кладутся варианты группы — контейнер с HorizontalLayoutGroup. " +
                 "Пусто — сама строка (варианты лягут прямо в неё).")]
        [SerializeField] private Transform _itemsRoot;

        [Tooltip("Скролл списка вариантов группы (ScrollRect на Items, родителе Items Root). " +
                 "Нужен кнопкам листания — без него Prev/Next ничего не будут делать.")]
        [SerializeField] private ScrollRect _scrollRect;

        [Tooltip("Кнопка «назад»: прокручивает список влево. Необязательна.")]
        [SerializeField] private Button _prevButton;

        [Tooltip("Кнопка «вперёд»: прокручивает список вправо. Необязательна.")]
        [SerializeField] private Button _nextButton;

        [Tooltip("Насколько прокручивать список за одно нажатие, в пикселях — примерно " +
                 "ширина одного варианта с отступом.")]
        [SerializeField] private float _scrollStep = 160f;

        [Tooltip("Кнопка «color»: открывает круглую палитру для перекраса зоны этой " +
                 "группы (см. ColorPaletteView). Необязательна — без неё строка просто " +
                 "не перекрашивается.")]
        [SerializeField] private Button _colorButton;

        [Tooltip("Подложка под кнопкой color. Скрывается и показывается вместе с " +
                 "кнопкой — см. SetColorAvailable.")]
        [SerializeField] private GameObject _colorbg;

        /// <summary>Куда панель кладёт варианты этой группы.</summary>
        public Transform ItemsRoot => _itemsRoot != null ? _itemsRoot : transform;

        /// <summary>Кнопка «color» этой строки. Панель сама решает, какую зону она откроет.</summary>
        public Button ColorButton => _colorButton;

        /// <summary>
        /// Показывает или скрывает кнопку color вместе с её подложкой — вызывается
        /// панелью, когда у группы нет зон для покраски (см. Skin Layout Editor —
        /// Color Zones).
        /// </summary>
        public void SetColorAvailable(bool available)
        {
            if (_colorButton != null)
                _colorButton.gameObject.SetActive(available);

            if (_colorbg != null)
                _colorbg.SetActive(available);
        }

        /// <summary>Подпись группы, обрезанная до <see cref="_titleMaxLength"/> символов.</summary>
        public void SetTitle(string title)
        {
            if (_title != null)
                _title.text = Truncate(title);
        }

        private string Truncate(string title)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;

            return _titleMaxLength > 0 && title.Length > _titleMaxLength
                ? title.Substring(0, _titleMaxLength)
                : title;
        }

        // =========================================================
        // ЛИСТАНИЕ
        // =========================================================

        /// <summary>
        /// Кнопки живут на самой строке и умирают вместе с ней (строку целиком
        /// сносит и пересоздаёт CustomizationPanel при смене категории), поэтому
        /// отписываться отдельно не нужно.
        /// </summary>
        private void Awake()
        {
            if (_prevButton != null)
                _prevButton.onClick.AddListener(() => Scroll(-1));

            if (_nextButton != null)
                _nextButton.onClick.AddListener(() => Scroll(1));
        }

        /// <summary>
        /// Прокрутить список на <see cref="_scrollStep"/> пикселей: -1 — влево,
        /// 1 — вправо. Списку скроллить некуда (все варианты и так помещаются) —
        /// нажатие ничего не делает, а не падает.
        /// </summary>
        private void Scroll(int direction)
        {
            if (_scrollRect == null || _scrollRect.content == null) return;

            // Viewport не задан — ScrollRect сам считает себя вьюпортом (см. NestedScrollRect),
            // но это приватная логика ScrollRect, наружу не смотрит — повторяем её здесь.
            var viewport = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : _scrollRect.transform as RectTransform;
            if (viewport == null) return;

            var maxScrollable = Mathf.Max(0f, _scrollRect.content.rect.width - viewport.rect.width);
            if (maxScrollable <= 0f) return;

            var deltaNormalized = _scrollStep / maxScrollable;

            _scrollRect.horizontalNormalizedPosition =
                Mathf.Clamp01(_scrollRect.horizontalNormalizedPosition + direction * deltaNormalized);
        }
    }
}
