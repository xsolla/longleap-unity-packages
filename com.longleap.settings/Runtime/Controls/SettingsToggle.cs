using LongLeap.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Settings.Controls
{
    public class SettingsToggle : SettingsToggleBase
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Toggle   _toggle;

        private ISettingsService _service;
        private Setting<bool> _setting;
        private string _labelKey;

        public override void Bind(ISettingsService service, Setting<bool> s)
        {
            _service = service;
            _setting = s;
            _labelKey = s.LabelKey;
            _label.text = Loc.Resolve(_labelKey);
            _toggle.isOn = s.Value;
            _toggle.onValueChanged.AddListener(OnChanged);
            Loc.OnLanguageChanged += OnLanguageChanged;
        }

        void OnChanged(bool v) => _service.SetValue(_setting, v);

        void OnLanguageChanged() => _label.text = Loc.Resolve(_labelKey);

        void OnDestroy()
        {
            _toggle.onValueChanged.RemoveListener(OnChanged);
            Loc.OnLanguageChanged -= OnLanguageChanged;
        }
    }
}
