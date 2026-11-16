using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// String field holding a setting key (<c>Setting.Key</c>). The inspector draws it as a
    /// dropdown of every key in the game, so a typo is not possible.
    /// </summary>
    public sealed class SettingKeyAttribute : PropertyAttribute { }

    /// <summary>
    /// What the settings window shows: sections, their rows and captions. Assigned on the
    /// settings window (for example <c>LongLeap.UI.SettingsWindow</c>); another window only needs another config.
    ///
    /// The config only decides what to show and how to label it. Values, ranges and defaults
    /// stay in the settings groups: a row refers to a setting by key, and the row view is chosen
    /// by the setting type — a number gives a slider, a bool gives a toggle, a key gives a rebind button.
    /// </summary>
    [CreateAssetMenu(menuName = "LongLeap/Settings Window Config", fileName = "SettingsWindowConfig")]
    public sealed class SettingsWindowConfig : ScriptableObject
    {
        [Header("Appearance")]

        [Tooltip("Window font. Empty: the window uses the TMP default font or the font of any other UI text.")]
        public TMP_FontAsset Font;

        [Tooltip("Panel size in pixels at the 1920x1080 reference resolution.")]
        public Vector2 PanelSize = new Vector2(900f, 980f);

        [Header("Window captions")]

        public string Title = "SETTINGS";

        public string ListeningText = "PRESS A KEY";

        public string ResetText = "DEFAULTS";

        public string BackText = "BACK";

        [Tooltip("Shown when no settings service has been bound (settings failed to load).")]
        public string UnavailableText = "Settings are unavailable in this session.";

        [Header("Sections")]

        public List<Section> Sections = new List<Section>();

        [Serializable]
        public sealed class Section
        {
            public string Title;

            [Tooltip("Hint under the section. Empty: no hint.")]
            public string Hint;

            [Tooltip("Hide on touch devices, which have no keyboard.")]
            public bool HideOnTouch;

            public List<Row> Rows = new List<Row>();
        }

        [Serializable]
        public sealed class Row
        {
            [SettingKey]
            [Tooltip("Setting key. An unknown key logs an error and the row is skipped.")]
            public string SettingKey;

            [Tooltip("Label used when localization is not ready or has no translation for the setting's LabelKey.")]
            public string Label;
        }
    }
}
