using System;
using System.Collections.Generic;
using LongLeap.Settings;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Settings.UI.Editor
{
    /// <summary>
    /// Ключ настройки выпадающим списком. Список собирается из всех классов
    /// <see cref="ISettingsGroup"/> с конструктором без параметров, которые есть
    /// в проекте, — новая группа или настройка появляется в нём сама. Ключ,
    /// которого нет, остаётся в списке красной строкой «missing», чтобы опечатку
    /// в конфиге было видно, а не потеряно молча.
    ///
    /// Группа с обязательными параметрами конструктора в список не попадёт:
    /// редактор не знает, чем её создавать. Её ключи в конфиге всё равно
    /// работают, просто выбираются как «missing» — или добавь группе пустой конструктор.
    /// </summary>
    [CustomPropertyDrawer(typeof(SettingKeyAttribute))]
    public sealed class SettingKeyDrawer : PropertyDrawer
    {
        private static string[] _keys;
        private static string[] _labels;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "[SettingKey] работает только со string");
                return;
            }

            EnsureKeys();

            string current = property.stringValue;
            int index = Array.IndexOf(_keys, current);
            bool missing = index < 0;

            // Лишняя последняя строка — текущее значение, которого нет в игре.
            var labels = new List<string>(_labels);
            if (missing)
                labels.Add(string.IsNullOrEmpty(current) ? "<not set>" : $"missing: {current}");

            EditorGUI.BeginProperty(position, label, property);

            var previousColor = GUI.color;
            if (missing)
                GUI.color = new Color(1f, 0.45f, 0.45f);

            EditorGUI.BeginChangeCheck();
            int selected = EditorGUI.Popup(position, label.text, missing ? labels.Count - 1 : index, labels.ToArray());

            GUI.color = previousColor;

            if (EditorGUI.EndChangeCheck() && selected < _keys.Length)
                property.stringValue = _keys[selected];

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Ключи берём у свежесозданных групп без сервиса и хранилища: редактору
        /// не нужен запущенный игровой контейнер, а список совпадает с тем, что
        /// регистрирует игра.
        /// </summary>
        private static void EnsureKeys()
        {
            if (_keys != null) return;

            var keys = new List<string>();
            var labels = new List<string>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<ISettingsGroup>())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                ISettingsGroup group;

                try
                {
                    group = (ISettingsGroup)Activator.CreateInstance(type);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Settings] не удалось создать группу {type.Name} для списка ключей: {e.Message}");
                    continue;
                }

                foreach (var setting in group.All)
                {
                    if (keys.Contains(setting.Key)) continue;

                    keys.Add(setting.Key);
                    labels.Add($"{setting.Key}  ({setting.ValueType.Name})");
                }
            }

            _keys = keys.ToArray();
            _labels = labels.ToArray();
        }
    }
}
