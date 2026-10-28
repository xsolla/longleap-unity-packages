using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Кирпичи, из которых окно настроек собирает себя в рантайме: текст, фон,
    /// ползунок, кнопка. В префабе окна лежит только пустой Root — всё
    /// содержимое строится кодом, поэтому новая строка настроек добавляется
    /// одной строкой в <see cref="SettingsPanel"/>, а не сборкой иерархии руками.
    ///
    /// Цвета и размеры собраны здесь: менять вид окна — значит править эти
    /// константы, а не искать числа по всем файлам.
    /// </summary>
    internal static class SettingsUi
    {
        // =========================================================
        // ПАЛИТРА И РАЗМЕРЫ
        // =========================================================

        /// <summary>Розовый из главного меню — заголовки и активные элементы.</summary>
        public static readonly Color Accent = new Color(0.698f, 0.302f, 0.576f, 1f);

        public static readonly Color Text = new Color(0.94f, 0.94f, 0.96f, 1f);

        public static readonly Color TextDim = new Color(0.68f, 0.68f, 0.74f, 1f);

        /// <summary>Затемнение под окном: закрывает меню и ловит клики мимо панели.</summary>
        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.78f);

        public static readonly Color PanelBackground = new Color(0.06f, 0.06f, 0.08f, 0.97f);

        public static readonly Color RowBackground = new Color(1f, 1f, 1f, 0.04f);

        public static readonly Color SliderTrack = new Color(1f, 1f, 1f, 0.16f);

        /// <summary>Кнопка клавиши, пока она ждёт нажатия.</summary>
        public static readonly Color Listening = new Color(0.85f, 0.62f, 0.18f, 1f);

        public const float RowHeight = 46f;

        public const float RowSpacing = 6f;

        public const int LabelFontSize = 24;

        public const int ValueFontSize = 22;

        // =========================================================
        // СБОРКА
        // =========================================================

        /// <summary>Пустой объект с RectTransform — узел иерархии UI.</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.layer = parent != null ? parent.gameObject.layer : 5;

            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;

            return rect;
        }

        /// <summary>Растянуть на весь родительский прямоугольник.</summary>
        public static RectTransform Stretch(this RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return rect;
        }

        /// <summary>
        /// Картинка-заливка. Клики она ловит, только если попросили: фон строки
        /// не должен перехватывать нажатия у ползунка и кнопок.
        /// </summary>
        public static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget = false)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;

            return image;
        }

        public static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string content,
            int fontSize,
            Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();

            // Без шрифта TMP не может построить текст: он лезет в материал
            // шрифта и роняет пересчёт раскладки всего окна (NullReference в
            // MaterialReference). Стандартного шрифта в проекте нет — TMP
            // Essentials не импортированы, — поэтому такой текст просто
            // выключаем: окно остаётся рабочим, но без подписи.
            if (font != null)
                text.font = font;
            else
                text.enabled = false;

            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;

            return text;
        }

        /// <summary>
        /// Вертикальная колонка: заголовки, секции и строки складываются сверху вниз.
        /// Высоту детей задают они сами (LayoutElement), ширину — колонка.
        /// </summary>
        public static VerticalLayoutGroup AddColumn(
            RectTransform rect, float spacing, RectOffset padding = null)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return layout;
        }

        /// <summary>Строка настройки: подпись слева, управление справа.</summary>
        public static HorizontalLayoutGroup AddRow(RectTransform rect, float spacing)
        {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(16, 16, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            return layout;
        }

        /// <summary>
        /// Размер элемента внутри раскладки. Ширина меньше нуля означает
        /// «не задано»: элемент тянется за flexibleWidth.
        /// </summary>
        public static LayoutElement SetSize(
            GameObject host,
            float preferredWidth = -1f,
            float preferredHeight = -1f,
            float flexibleWidth = 0f)
        {
            var element = host.GetComponent<LayoutElement>();
            if (element == null)
                element = host.AddComponent<LayoutElement>();

            element.preferredWidth = preferredWidth;
            element.minWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.minHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;

            return element;
        }

        /// <summary>
        /// Ползунок 0..1 со стандартной внутренней иерархией: фон, заливка и
        /// ручка. Раскладка родителя управляет только самим ползунком — его
        /// внутренности держатся на якорях.
        /// </summary>
        public static Slider CreateSlider(string name, Transform parent, Color fillColor)
        {
            var rect = CreateRect(name, parent);
            var slider = rect.gameObject.AddComponent<Slider>();

            var background = CreateImage("Background", rect, SliderTrack, raycastTarget: true);
            Bar(background.rectTransform, height: 8f);

            var fillArea = CreateRect("Fill Area", rect);
            Bar(fillArea, height: 8f);

            var fill = CreateImage("Fill", fillArea, fillColor);
            fill.rectTransform.Stretch();

            var handleArea = CreateRect("Handle Slide Area", rect);
            handleArea.Stretch();
            handleArea.offsetMin = new Vector2(11f, 0f);
            handleArea.offsetMax = new Vector2(-11f, 0f);

            var handle = CreateImage("Handle", handleArea, Text, raycastTarget: true);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(22f, -6f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.ColorTint;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;

            return slider;
        }

        /// <summary>Кнопка с подписью: назад, сброс, клавиша в строке управления.</summary>
        public static Button CreateButton(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string caption,
            Color background,
            out TextMeshProUGUI label)
        {
            var image = CreateImage(name, parent, background, raycastTarget: true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            label = CreateText(
                "Label", image.rectTransform, font, caption, ValueFontSize, Text,
                TextAlignmentOptions.Center);
            label.rectTransform.Stretch();

            return button;
        }

        /// <summary>Горизонтальная полоса заданной высоты по центру родителя.</summary>
        private static void Bar(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }
    }
}
