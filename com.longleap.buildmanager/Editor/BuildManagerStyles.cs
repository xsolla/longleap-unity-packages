using UnityEditor;
using UnityEngine;

namespace LongLeap.BuildManager.Editor
{
    // Styling for the highlight of changed rows.
    //
    // The highlight does NOT rely on colour alone. There are four signals, and
    // three of them work in greyscale:
    //
    //   1. a bar on the left (shape, visible at any contrast);
    //   2. a row fill (colour, the weakest signal, only there to catch the eye
    //      while scrolling);
    //   3. the "*" marker and the "(off -> on)" suffix (text, which also says
    //      WHAT will change, not just that something changed);
    //   4. an asterisk in the window title.
    //
    // The colours differ between the two editor themes: the same blue looks
    // muddy on the light theme and is indistinguishable on the dark one.
    internal static class BuildManagerStyles
    {
        public const float AccentBarWidth = 3f;
        public const float RowPadding = 2f;
        public const float MarkerWidth = 14f;
        public const float SectionIndent = 6f;

        public static Color Accent =>
            EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.60f, 0.95f)
                : new Color(0.15f, 0.40f, 0.85f);

        public static Color RowTint
        {
            get
            {
                Color accent = Accent;
                return new Color(accent.r, accent.g, accent.b, 0.10f);
            }
        }

        private static GUIStyle _dirtyLabel;
        private static GUIStyle _marker;
        private static GUIStyle _reason;
        private static GUIStyle _sectionHeader;
        private static GUIStyle _presetSelected;

        public static GUIStyle DirtyLabel => _dirtyLabel ?? (_dirtyLabel =
            new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });

        public static GUIStyle Marker => _marker ?? (_marker =
            new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft });

        // The reason a row is disabled. It goes under the row, indented, because
        // it belongs to the row and not to the block.
        public static GUIStyle Reason => _reason ?? (_reason =
            new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                padding = new RectOffset(18, 4, 0, 2),
            });

        public static GUIStyle SectionHeader => _sectionHeader ?? (_sectionHeader =
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 });

        public static GUIStyle PresetSelected => _presetSelected ?? (_presetSelected =
            new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold });

        // Drop the cached styles when the theme changes: a GUIStyle remembers
        // the colours of the theme it was created in.
        public static void Invalidate()
        {
            _dirtyLabel = null;
            _marker = null;
            _reason = null;
            _sectionHeader = null;
            _presetSelected = null;
        }

        public static void DrawDirtyBackground(Rect row)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(row, RowTint);
            EditorGUI.DrawRect(new Rect(row.x, row.y, AccentBarWidth, row.height), Accent);
        }
    }
}
