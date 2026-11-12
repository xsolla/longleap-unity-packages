#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LongLeap.Skins.EditorTools
{
    /// <summary>
    /// Зона перекраса в инспекторе: имя, цвет и список слотов с окном выбора
    /// (<see cref="SlotListField"/>). Тот же drawer рисует зоны и в панели конфига
    /// Skin Layout Editor.
    /// </summary>
    [CustomPropertyDrawer(typeof(SpineAppearanceConfig.ColorZone))]
    internal sealed class ColorZoneDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;

            var step = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var slots = property.FindPropertyRelative(nameof(SpineAppearanceConfig.ColorZone.Slots));

            return step * 3f + SlotListField.GetHeight(slots);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var name = property.FindPropertyRelative(nameof(SpineAppearanceConfig.ColorZone.Name));
            var color = property.FindPropertyRelative(nameof(SpineAppearanceConfig.ColorZone.Color));
            var slots = property.FindPropertyRelative(nameof(SpineAppearanceConfig.ColorZone.Slots));

            var title = string.IsNullOrWhiteSpace(name.stringValue) ? label.text : name.stringValue;

            EditorGUI.BeginProperty(position, label, property);

            try
            {
                var step = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

                property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, new GUIContent(title), true);
                if (!property.isExpanded) return;

                using (new EditorGUI.IndentLevelScope())
                {
                    row.y += step;
                    EditorGUI.PropertyField(row, name);

                    row.y += step;
                    EditorGUI.PropertyField(row, color);

                    row.y += step;
                    var slotsRect = new Rect(row.x, row.y, row.width, SlotListField.GetHeight(slots));
                    SlotListField.Draw(slotsRect, slots, SlotListField.FindSkeleton(property), $"Зона «{title}»");
                }
            }
            finally
            {
                // Окно выбора прерывает проход GUI исключением — свойство всё равно закрываем.
                EditorGUI.EndProperty();
            }
        }
    }

    /// <summary>
    /// Правило перекрытия в инспекторе: скин и список слотов с окном выбора
    /// (<see cref="SlotListField"/>). Тот же drawer рисует правила и в панели конфига
    /// Skin Layout Editor.
    /// </summary>
    [CustomPropertyDrawer(typeof(SpineAppearanceConfig.HideRule))]
    internal sealed class HideRuleDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;

            var step = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var slots = property.FindPropertyRelative(nameof(SpineAppearanceConfig.HideRule.Slots));

            return step * 2f + SlotListField.GetHeight(slots);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var skin = property.FindPropertyRelative(nameof(SpineAppearanceConfig.HideRule.Skin));
            var slots = property.FindPropertyRelative(nameof(SpineAppearanceConfig.HideRule.Slots));

            var title = string.IsNullOrEmpty(skin.stringValue) ? "(скин не выбран)" : skin.stringValue;

            EditorGUI.BeginProperty(position, label, property);

            try
            {
                var step = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

                property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, new GUIContent(title), true);
                if (!property.isExpanded) return;

                using (new EditorGUI.IndentLevelScope())
                {
                    row.y += step;
                    EditorGUI.PropertyField(row, skin);

                    row.y += step;
                    var slotsRect = new Rect(row.x, row.y, row.width, SlotListField.GetHeight(slots));
                    SlotListField.Draw(slotsRect, slots, SlotListField.FindSkeleton(property), $"Прячет: {title}");
                }
            }
            finally
            {
                EditorGUI.EndProperty();
            }
        }
    }
}
#endif
