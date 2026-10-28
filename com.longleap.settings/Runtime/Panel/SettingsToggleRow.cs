using LongLeap.Localization;
using LongLeap.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Строка-переключатель для настройки <c>bool</c>: подпись слева, кнопка
    /// ON/OFF справа. Иерархию строит сама (<see cref="Create"/>) — окно
    /// настроек собирается кодом, как и остальные строки.
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
            var rect = SettingsUi.CreateRect("ToggleRow", parent);
            var row = rect.gameObject.AddComponent<SettingsToggleRow>();

            var background = rect.gameObject.AddComponent<Image>();
            background.color = SettingsUi.RowBackground;
            background.raycastTarget = false;

            SettingsUi.AddRow(rect, spacing: 14f);
            SettingsUi.SetSize(rect.gameObject, preferredHeight: SettingsUi.RowHeight);

            row._fallbackLabel = fallbackLabel;

            row._label = SettingsUi.CreateText(
                "Label", rect, font, fallbackLabel, SettingsUi.LabelFontSize, SettingsUi.Text);
            SettingsUi.SetSize(row._label.gameObject, flexibleWidth: 1f);

            row._button = SettingsUi.CreateButton(
                "Toggle", rect, font, "", new Color(1f, 1f, 1f, 0.10f), out row._value);
            SettingsUi.SetSize(row._button.gameObject, preferredWidth: 130f, preferredHeight: 34f);

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

        /// <summary>Показать текущее значение, если оно поменялось не здесь — например, «по умолчанию».</summary>
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
