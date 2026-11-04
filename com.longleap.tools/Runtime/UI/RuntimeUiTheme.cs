using TMPro;
using UnityEngine;

namespace LongLeap.Tools.UI
{
    /// <summary>
    /// The look of windows built by <see cref="RuntimeUi"/>: palette, row sizes and the default
    /// font. Each game has its own: put an asset named <see cref="ResourcesName"/> into a
    /// <c>Resources</c> folder, or set <see cref="RuntimeUi.Theme"/> before windows are built.
    /// Without an asset the defaults below apply (the palette the package started with).
    /// </summary>
    [CreateAssetMenu(menuName = "LongLeap/Runtime UI Theme", fileName = ResourcesName)]
    public sealed class RuntimeUiTheme : ScriptableObject
    {
        /// <summary>Name of the asset in <c>Resources</c> that <see cref="RuntimeUi"/> loads on its own.</summary>
        public const string ResourcesName = "RuntimeUiTheme";

        [Header("Palette")]
        [Tooltip("Headers and active elements.")]
        public Color Accent = new Color(0.698f, 0.302f, 0.576f, 1f);

        [Tooltip("Main text.")]
        public Color Text = new Color(0.94f, 0.94f, 0.96f, 1f);

        [Tooltip("Secondary text: hints, values.")]
        public Color TextDim = new Color(0.68f, 0.68f, 0.74f, 1f);

        [Tooltip("Dimming under a window: covers the menu and catches clicks outside the panel.")]
        public Color Backdrop = new Color(0f, 0f, 0f, 0.78f);

        [Tooltip("Window panel background.")]
        public Color PanelBackground = new Color(0.06f, 0.06f, 0.08f, 0.97f);

        [Tooltip("Settings row background.")]
        public Color RowBackground = new Color(1f, 1f, 1f, 0.04f);

        [Tooltip("Slider track.")]
        public Color SliderTrack = new Color(1f, 1f, 1f, 0.16f);

        [Tooltip("Key binding button while it waits for a key press.")]
        public Color Listening = new Color(0.85f, 0.62f, 0.18f, 1f);

        [Tooltip("Button tint multiplier under the cursor (ColorTint).")]
        public Color ButtonHighlighted = new Color(1f, 1f, 1f, 1f);

        [Tooltip("Button tint multiplier while pressed (ColorTint).")]
        public Color ButtonPressed = new Color(0.8f, 0.8f, 0.8f, 1f);

        [Header("Sizes")]
        [Tooltip("Height of a settings row, px.")]
        public float RowHeight = 46f;

        [Tooltip("Vertical gap between rows, px.")]
        public float RowSpacing = 6f;

        [Tooltip("Font size of row labels.")]
        public int LabelFontSize = 24;

        [Tooltip("Font size of values and button captions.")]
        public int ValueFontSize = 22;

        [Header("Font")]
        [Tooltip("Text font when the caller passes none. Empty and none passed — the text is disabled.")]
        public TMP_FontAsset Font;
    }
}
