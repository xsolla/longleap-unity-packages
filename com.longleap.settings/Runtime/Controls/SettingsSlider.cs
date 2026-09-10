using LongLeap.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Settings.Controls
{
    public class SettingsSlider : SettingsSliderBase
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Slider   _slider;

        private ISettingsService _service;
        private UnityEngine.Events.UnityAction<float> _callback;
        private string _labelKey;

        public override void Bind(ISettingsService service, NumericSetting<float> s)
            => BindCore(service, s.LabelKey, s.Min, s.Max, s.Value, false, v => _service.SetValue(s, v));

        public override void Bind(ISettingsService service, NumericSetting<int> s)
            => BindCore(service, s.LabelKey, s.Min, s.Max, s.Value, true, v => _service.SetValue(s, (int)v));

        private void BindCore(ISettingsService service, string labelKey, float min, float max, float value,
            bool wholeNumbers, UnityEngine.Events.UnityAction<float> onChanged)
        {
            _service = service;
            _labelKey = labelKey;
            _label.text = Loc.Resolve(_labelKey);
            _slider.wholeNumbers = wholeNumbers;
            _slider.minValue = min;
            _slider.maxValue = max;
            _slider.value = value;
            _callback = onChanged;
            _slider.onValueChanged.AddListener(_callback);
            Loc.OnLanguageChanged += OnLanguageChanged;
        }

        void OnLanguageChanged() => _label.text = Loc.Resolve(_labelKey);

        void OnDestroy()
        {
            if (_callback != null)
                _slider.onValueChanged.RemoveListener(_callback);
            Loc.OnLanguageChanged -= OnLanguageChanged;
        }
    }
}
