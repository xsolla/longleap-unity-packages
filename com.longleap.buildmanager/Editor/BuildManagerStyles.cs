using UnityEditor;
using UnityEngine;

namespace LongLeap.BuildManager.Editor
{
    // Оформление подсветки изменённых строк.
    //
    // Подсветка НЕ опирается на цвет в одиночку — сигналов четыре, и три из
    // них работают в оттенках серого:
    //
    //   1. полоска слева (форма, видна при любом контрасте);
    //   2. заливка строки (цвет, самый слабый сигнал, нужен только чтобы
    //      взгляд цеплялся при прокрутке);
    //   3. маркер «*» и приписка «(off -> on)» (текст — и он ещё и говорит,
    //      ЧТО именно изменится, а не только что что-то изменилось);
    //   4. звёздочка в заголовке окна.
    //
    // Цвета разные для двух тем редактора: один и тот же синий на светлой
    // теме выглядит грязным, на тёмной — неразличимым.
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

        // Причина, по которой строка погашена. Идёт под строкой с отступом,
        // потому что относится к ней, а не к блоку.
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

        // Сбрасываем кэш стилей при смене темы: GUIStyle помнит цвета темы, в
        // которой был создан.
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
