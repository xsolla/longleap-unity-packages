using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Tools.UI
{
    /// <summary>
    /// Building blocks for UI assembled at runtime: text, background, slider, button. The
    /// settings panel builds itself with them (<c>LongLeap.Settings.UI.SettingsPanel</c>; the
    /// window prefab holds only an empty Root), and the UI package windows draw their extra
    /// parts with them — so a new row is added in code, not by assembling a hierarchy by hand.
    ///
    /// One class for every package: settings and UI used to have their own copies, and the
    /// windows drifted apart whenever only one was edited. Colors, sizes and the default font
    /// come from the game's theme (<see cref="Theme"/>, a <see cref="RuntimeUiTheme"/> asset):
    /// changing the look means editing the theme, not hunting for numbers across files.
    /// </summary>
    public static class RuntimeUi
    {
        // =========================================================
        // THEME
        // =========================================================

        private const int DefaultUiLayer = 5;

        private static RuntimeUiTheme _theme;
        private static bool _warnedNoFont;

        /// <summary>
        /// Window theme. Without an explicit value — the game's <c>Resources/RuntimeUiTheme</c>
        /// asset, and if there is none — the <see cref="RuntimeUiTheme"/> defaults. Set it before
        /// windows are built if the theme is not in Resources; null — look in Resources again.
        /// </summary>
        public static RuntimeUiTheme Theme
        {
            get
            {
                if (_theme == null)
                    _theme = LoadTheme();

                return _theme;
            }
            set => _theme = value;
        }

        /// <summary>Headers and active elements.</summary>
        public static Color Accent => Theme.Accent;

        /// <summary>Main text.</summary>
        public static Color Text => Theme.Text;

        /// <summary>Secondary text: hints, values.</summary>
        public static Color TextDim => Theme.TextDim;

        /// <summary>Dimming under a window: covers the menu and catches clicks outside the panel.</summary>
        public static Color Backdrop => Theme.Backdrop;

        /// <summary>Window panel background.</summary>
        public static Color PanelBackground => Theme.PanelBackground;

        /// <summary>Settings row background.</summary>
        public static Color RowBackground => Theme.RowBackground;

        /// <summary>Slider track.</summary>
        public static Color SliderTrack => Theme.SliderTrack;

        /// <summary>Key binding button while it waits for a key press.</summary>
        public static Color Listening => Theme.Listening;

        /// <summary>Height of a settings row.</summary>
        public static float RowHeight => Theme.RowHeight;

        /// <summary>Vertical gap between rows.</summary>
        public static float RowSpacing => Theme.RowSpacing;

        /// <summary>Font size of row labels.</summary>
        public static int LabelFontSize => Theme.LabelFontSize;

        /// <summary>Font size of values and button captions.</summary>
        public static int ValueFontSize => Theme.ValueFontSize;

        private static RuntimeUiTheme LoadTheme()
        {
            var theme = Resources.Load<RuntimeUiTheme>(RuntimeUiTheme.ResourcesName);
            if (theme != null)
                return theme;

            theme = ScriptableObject.CreateInstance<RuntimeUiTheme>();
            theme.name = RuntimeUiTheme.ResourcesName + " (default)";
            theme.hideFlags = HideFlags.HideAndDontSave;

            return theme;
        }

        // Statics survive entering Play Mode without a domain reload: a theme set in the
        // previous run must not leak into the next one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _theme = null;
            _warnedNoFont = false;
        }

        // =========================================================
        // BUILDING
        // =========================================================

        /// <summary>Empty object with a RectTransform — a node of the UI hierarchy.</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.layer = parent != null ? parent.gameObject.layer : UiLayer();

            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;

            return rect;
        }

        /// <summary>Stretch over the whole parent rectangle.</summary>
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
        /// Solid fill image. It catches clicks only when asked to: a row backgrпound must not
        /// steal presses from the slider and buttons.
        /// </summary>
        public static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget = false)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;

            return image;
        }

        /// <summary>
        /// Single-line text that ends with an ellipsis when it does not fit.
        /// <paramref name="font"/> null — the theme font (<see cref="RuntimeUiTheme.Font"/>).
        /// </summary>
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

            if (font == null)
                font = Theme.Font;

            // Without a font TMP cannot build the text: it reaches into the font material and
            // breaks the layout rebuild of the whole window (NullReference in MaterialReference).
            // The project may have no default font — TMP Essentials are not imported — so such
            // text is disabled: the window keeps working, just without the caption. Warn once.
            if (font != null)
            {
                text.font = font;
            }
            else
            {
                text.enabled = false;

                if (!_warnedNoFont)
                {
                    _warnedNoFont = true;
                    Debug.LogWarning($"RuntimeUi: no font for text \"{name}\" — such texts are hidden. " +
                                     $"Pass a font or set Font in the {nameof(RuntimeUiTheme)} asset.");
                }
            }

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
        /// Vertical column: headers, sections and rows stack top to bottom.
        /// Children set their own height (LayoutElement); the column sets their width.
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

        /// <summary>Settings row: label on the left, control on the right.</summary>
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
        /// Size of an element inside a layout. A width below zero means "not set": the element
        /// stretches by flexibleWidth.
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
        /// 0..1 slider with the standard inner hierarchy: background, fill and handle. The
        /// parent layout controls only the slider itself — its insides hold on anchors.
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

        /// <summary>Button with a caption: back, reset, a key in a controls row.</summary>
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
            colors.highlightedColor = Theme.ButtonHighlighted;
            colors.pressedColor = Theme.ButtonPressed;
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            label = CreateText(
                "Label", image.rectTransform, font, caption, ValueFontSize, Text,
                TextAlignmentOptions.Center);
            label.rectTransform.Stretch();

            return button;
        }

        /// <summary>Horizontal bar of the given height, centered in the parent.</summary>
        private static void Bar(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }

        // The "UI" layer, or Unity's default index 5 if the project renamed it.
        private static int UiLayer()
        {
            int layer = LayerMask.NameToLayer("UI");
            return layer >= 0 ? layer : DefaultUiLayer;
        }
    }
}
