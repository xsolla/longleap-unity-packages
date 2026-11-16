using LongLeap.Localization;
using LongLeap.Settings;
using LongLeap.Tools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Toggle row for a <c>bool</c> setting: label on the left, ON/OFF button on the right.
    /// Builds its own hierarchy (<see cref="Create"/>), like the other rows of the code-built panel.
    /// </summary>
    public sealed class SettingsToggleRow : MonoBehaviour
    {
        private ISettingsService _service;
        private Setting<bool> _setting;

        private Button _button;
        private TextMeshProUGUI _label;
        private TextMeshProUGUI _value;

        private string _labelKey;
        private string _fallbackLabel;

        public static SettingsToggleRow Create(Transform parent, TMP_FontAsset font, string fallbackLabel)
        {
            var rect = RuntimeUi.CreateRect("ToggleRow", parent);
            var row = rect.gameObject.AddComponent<SettingsToggleRow>();

            var background = rect.gameObject.AddComponent<Image>();
            background.color = RuntimeUi.RowBackground;
            background.raycastTarget = false;

            RuntimeUi.AddRow(rect, spacing: 14f);
            RuntimeUi.SetSize(rect.gameObject, preferredHeight: RuntimeUi.RowHeight);

            row._fallbackLabel = fallbackLabel;

            row._label = RuntimeUi.CreateText(
                "Label", rect, font, fallbackLabel, RuntimeUi.LabelFontSize, RuntimeUi.Text);
            RuntimeUi.SetSize(row._label.gameObject, flexibleWidth: 1f);

            row._button = RuntimeUi.CreateButton(
                "Toggle", rect, font, "", new Color(1f, 1f, 1f, 0.10f), out row._value);
            RuntimeUi.SetSize(row._button.gameObject, preferredWidth: 130f, preferredHeight: 34f);

            row._button.onClick.AddListener(row.OnClicked);

            return row;
        }

        public void Bind(ISettingsService service, Setting<bool> setting)
        {
            _service = service;
            _setting = setting;
            _labelKey = setting != null ? setting.LabelKey : null;

            ShowLabel();
            Refresh();

            Loc.OnLanguageChanged += ShowLabel;
        }

        /// <summary>Show the current value when it changed elsewhere, for example after "defaults".</summary>
        public void Refresh()
        {
            if (_value == null || _setting == null) return;

            _value.text = _setting.Value ? "ON" : "OFF";
        }

        private void OnClicked()
        {
            if (_service == null || _setting == null) return;

            _service.SetValue(_setting, !_setting.Value);
        }

        private void ShowLabel()
        {
            if (_label == null) return;

            string translated = Loc.IsReady && !string.IsNullOrEmpty(_labelKey)
                ? Loc.Resolve(_labelKey)
                : null;
            _label.text = string.IsNullOrWhiteSpace(translated) || translated == _labelKey
                ? _fallbackLabel
                : translated;
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnClicked);

            Loc.OnLanguageChanged -= ShowLabel;
        }
    }
}
