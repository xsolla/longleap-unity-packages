#if UNITY_EDITOR
using System;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Skins.EditorTools
{
    /// <summary>
    /// Список слотов (<c>string[] Slots</c> зоны перекраса или правила перекрытия) в
    /// инспекторе: заголовок «Slots (N)» с кнопкой окна выбора
    /// (<see cref="SlotPickerPopup"/>), а в раскрытом виде — по строке на слот с
    /// крестиком. Слот, которого больше нет в скелете, подсвечен красным.
    ///
    /// Рисуется из drawer'ов зоны и правила, а не атрибутом на массиве: drawer
    /// атрибута Unity применяет к каждому элементу, а не к списку целиком.
    /// </summary>
    internal static class SlotListField
    {
        private const float PickButtonWidth = 80f;
        private const float RemoveButtonWidth = 20f;
        private const float RowIndent = 15f;

        private static GUIStyle _missingStyle;

        private static GUIStyle MissingStyle => _missingStyle ??= new GUIStyle(EditorStyles.label)
        {
            normal = { textColor = new Color(0.9f, 0.35f, 0.3f) },
        };

        public static float GetHeight(SerializedProperty slots)
        {
            var line = EditorGUIUtility.singleLineHeight;
            var step = line + EditorGUIUtility.standardVerticalSpacing;

            return slots.isExpanded ? line + slots.arraySize * step : line;
        }

        /// <param name="rect">Место под список, высотой <see cref="GetHeight"/>.</param>
        /// <param name="slots">Массив имён слотов.</param>
        /// <param name="skeleton">Откуда брать слоты; null — кнопка выбора выключена.</param>
        /// <param name="pickerTitle">Заголовок окна выбора: что именно правится.</param>
        public static void Draw(Rect rect, SerializedProperty slots, SkeletonDataAsset skeleton, string pickerTitle)
        {
            var line = EditorGUIUtility.singleLineHeight;
            var step = line + EditorGUIUtility.standardVerticalSpacing;
            var count = slots.arraySize;

            var header = new Rect(rect.x, rect.y, rect.width, line);
            var pickRect = new Rect(header.xMax - PickButtonWidth, header.y, PickButtonWidth, line);
            var foldRect = new Rect(header.x, header.y, Mathf.Min(EditorGUIUtility.labelWidth, pickRect.x - header.x), line);

            slots.isExpanded = EditorGUI.Foldout(
                foldRect, slots.isExpanded, new GUIContent($"Slots ({count})", slots.tooltip), true);

            // Свёрнутый список — одной строкой, чтобы было видно, что в нём, не раскрывая.
            var summaryRect = new Rect(foldRect.xMax + 2f, header.y, pickRect.x - foldRect.xMax - 6f, line);
            if (!slots.isExpanded && count > 0 && summaryRect.width > 20f)
                GUI.Label(summaryRect, Summary(slots), EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(skeleton == null))
            {
                var pick = new GUIContent(
                    "Выбрать…",
                    skeleton == null
                        ? "Укажи Skeleton Data в конфиге — без него неоткуда взять слоты."
                        : "Окно выбора слотов: поиск, слоты по костям, галочки.");

                if (GUI.Button(pickRect, pick, EditorStyles.miniButton))
                    OpenPicker(pickRect, slots, skeleton, pickerTitle);
            }

            if (!slots.isExpanded) return;

            var data = skeleton != null ? skeleton.GetSkeletonData(true) : null;
            var x = EditorGUI.IndentedRect(header).x + RowIndent;

            for (int i = 0; i < count; i++)
            {
                var y = header.y + step * (i + 1);
                var name = slots.GetArrayElementAtIndex(i).stringValue;
                var missing = data != null && data.FindSlot(name) == null;

                var removeRect = new Rect(header.xMax - RemoveButtonWidth, y, RemoveButtonWidth, line);
                var nameRect = new Rect(x, y, removeRect.x - x - 4f, line);

                var content = missing
                    ? new GUIContent($"{name}   (нет в скелете)", "Слот переименован или удалён в Spine — игра его не найдёт.")
                    : new GUIContent(name);

                GUI.Label(nameRect, content, missing ? MissingStyle : EditorStyles.label);

                if (GUI.Button(removeRect, new GUIContent("×", "Убрать слот из списка"), EditorStyles.miniButton))
                {
                    slots.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }

        /// <summary>Скелет, к которому относится конфиг, — чтобы предлагать его слоты.</summary>
        public static SkeletonDataAsset FindSkeleton(SerializedProperty property)
        {
            return property.serializedObject.targetObject is IHasSkeletonDataAsset owner
                ? owner.SkeletonDataAsset
                : null;
        }

        private static string Summary(SerializedProperty slots)
        {
            var names = new string[slots.arraySize];
            for (int i = 0; i < names.Length; i++)
                names[i] = slots.GetArrayElementAtIndex(i).stringValue;

            return string.Join(", ", names);
        }

        /// <summary>
        /// Окно выбора живёт дольше прохода GUI, а <paramref name="slots"/> действителен
        /// только в нём, — поэтому окно получает свой <see cref="SerializedObject"/> и путь
        /// к массиву. Запись через него даёт Undo и метку изменения ассета.
        /// </summary>
        private static void OpenPicker(Rect activator, SerializedProperty slots, SkeletonDataAsset skeleton, string title)
        {
            var serialized = new SerializedObject(slots.serializedObject.targetObjects);
            var path = slots.propertyPath;

            SlotPickerPopup.Show(
                activator,
                skeleton,
                title,
                () =>
                {
                    serialized.Update();
                    var array = serialized.FindProperty(path);
                    if (array == null || !array.isArray) return Array.Empty<string>();

                    var names = new string[array.arraySize];
                    for (int i = 0; i < names.Length; i++)
                        names[i] = array.GetArrayElementAtIndex(i).stringValue;

                    return names;
                },
                names =>
                {
                    serialized.Update();
                    var array = serialized.FindProperty(path);
                    if (array == null || !array.isArray) return;

                    array.arraySize = names.Length;
                    for (int i = 0; i < names.Length; i++)
                        array.GetArrayElementAtIndex(i).stringValue = names[i];

                    serialized.ApplyModifiedProperties();
                });
        }
    }
}
#endif
