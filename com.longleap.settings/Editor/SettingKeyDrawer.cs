using System;
using System.Collections.Generic;
using LongLeap.Settings;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Settings.UI.Editor
{
    /// <summary>
    /// Draws a setting key as a dropdown. The list is built from every <see cref="ISettingsGroup"/>
    /// class in the project that has a parameterless constructor, so a new group or setting shows
    /// up on its own. A key that does not exist stays in the list as a red "missing" entry, so a
    /// typo in the config is visible instead of silently lost.
    ///
    /// A group whose constructor requires arguments is not listed: the editor does not know how to
    /// create it. Its keys still work in the config, they just show as "missing" — or give the
    /// group a parameterless constructor.
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
                EditorGUI.LabelField(position, label.text, "[SettingKey] works only with string fields");
                return;
            }

            EnsureKeys();

            string current = property.stringValue;
            int index = Array.IndexOf(_keys, current);
            bool missing = index < 0;

            // Extra last entry: the current value, which does not exist in the game.
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
        /// Keys come from freshly created groups, with no service or storage: the editor does not
        /// need a running game container, and the list matches what the game registers.
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
                    Debug.LogWarning($"[Settings] failed to create group {type.Name} for the key list: {e.Message}");
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
