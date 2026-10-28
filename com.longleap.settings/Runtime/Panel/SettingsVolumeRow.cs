using LongLeap.Localization;
using LongLeap.Settings;
using LongLeap.Settings.Controls;
using LongLeap.Tools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Строка громкости: подпись, ползунок и процент справа.
    ///
    /// Наследуется от <see cref="SettingsSliderBase"/> из пакета настроек, то
    /// есть говорит с настройкой ровно тем же контрактом, что и обычный
    /// префабный ползунок. Разница одна: иерархию строит сама
    /// (<see cref="Create"/>), потому что окно настроек собирается
    /// кодом, а не префабами.
    ///
    /// Значение уезжает в настройку на каждое движение ползунка — звук меняется,
    /// пока его тянут (см. SoundSettingsBinder). В хранилище оно попадает позже,
    /// когда окно закроют.
    /// </summary>
    public sealed class SettingsVolumeRow : SettingsSliderBase
    {
        private ISettingsService _service;
        private Slider _slider;
        private TextMeshProUGUI _label;
        private TextMeshProUGUI _value;

        private string _labelKey;
        private string _fallbackLabel;

        private NumericSetting<float> _floatSetting;
        private NumericSetting<int> _intSetting;

        /// <summary>
        /// Собрать строку внутри колонки окна. <paramref name="fallbackLabel"/> —
        /// текст, пока локализация не поднята: таблиц в проекте ещё нет, и без
        /// него в окне были бы видны ключи.
        /// </summary>
        public static SettingsVolumeRow Create(Transform parent, TMP_FontAsset font, string fallbackLabel)
        {
            var rect = RuntimeUi.CreateRect("VolumeRow", parent);
            var row = rect.gameObject.AddComponent<SettingsVolumeRow>();

            var background = rect.gameObject.AddComponent<Image>();
            background.color = RuntimeUi.RowBackground;
            background.raycastTarget = false;

            RuntimeUi.AddRow(rect, spacing: 14f);
            RuntimeUi.SetSize(rect.gameObject, preferredHeight: RuntimeUi.RowHeight);

            row._fallbackLabel = fallbackLabel;

            row._label = RuntimeUi.CreateText(
                "Label", rect, font, fallbackLabel, RuntimeUi.LabelFontSize, RuntimeUi.Text);
            RuntimeUi.SetSize(row._label.gameObject, flexibleWidth: 1f);

            row._slider = RuntimeUi.CreateSlider("Slider", rect, RuntimeUi.Accent);
            RuntimeUi.SetSize(row._slider.gameObject, preferredWidth: 340f, preferredHeight: 26f);

            row._value = RuntimeUi.CreateText(
                "Value", rect, font, "100%", RuntimeUi.ValueFontSize, RuntimeUi.TextDim,
                TextAlignmentOptions.Right);
            RuntimeUi.SetSize(row._value.gameObject, preferredWidth: 78f);

            return row;
        }

        public override void Bind(ISettingsService service, NumericSetting<float> setting)
        {
            if (setting == null) return;

            _floatSetting = setting;

            BindCore(service, setting.LabelKey, setting.Min, setting.Max, setting.Value, wholeNumbers: false);
        }

        public override void Bind(ISettingsService service, NumericSetting<int> setting)
        {
            if (setting == null) return;

            _intSetting = setting;

            BindCore(service, setting.LabelKey, setting.Min, setting.Max, setting.Value, wholeNumbers: true);
        }

        private void BindCore(
            ISettingsService service,
            string labelKey,
            float min,
            float max,
            float value,
            bool wholeNumbers)
        {
            _service = service;
            _labelKey = labelKey;

            ShowLabel();

            _slider.wholeNumbers = wholeNumbers;
            _slider.minValue = min;
            _slider.maxValue = max;
            _slider.SetValueWithoutNotify(value);

            ShowValue(value);

            _slider.onValueChanged.AddListener(OnSliderChanged);
            Loc.OnLanguageChanged += ShowLabel;
        }

        /// <summary>
        /// Показать значение, которое пришло не из ползунка, — например, после
        /// кнопки «по умолчанию».
        /// </summary>
        public void Refresh()
        {
            if (_slider == null) return;

            float value = _floatSetting != null
                ? _floatSetting.Value
                : _intSetting != null
                    ? _intSetting.Value
                    : _slider.value;

            _slider.SetValueWithoutNotify(value);
            ShowValue(value);
        }

        private void OnSliderChanged(float value)
        {
            if (_service == null) return;

            if (_floatSetting != null)
                _service.SetValue(_floatSetting, value);
            else if (_intSetting != null)
                _service.SetValue(_intSetting, Mathf.RoundToInt(value));

            ShowValue(value);
        }

        /// <summary>
        /// Диапазон 0..1 показываем процентами — так громкость читается привычнее,
        /// чем «0,73». Остальные диапазоны — как есть.
        /// </summary>
        private void ShowValue(float value)
        {
            if (_value == null) return;

            bool normalized = Mathf.Approximately(_slider.minValue, 0f) &&
                              Mathf.Approximately(_slider.maxValue, 1f);

            _value.text = normalized
                ? Mathf.RoundToInt(value * 100f) + "%"
                : Mathf.RoundToInt(value).ToString();
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
            if (_slider != null)
                _slider.onValueChanged.RemoveListener(OnSliderChanged);

            Loc.OnLanguageChanged -= ShowLabel;
        }
    }
}
