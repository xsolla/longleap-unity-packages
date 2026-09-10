using System.Collections.Generic;
using LongLeap.Localization;
using TMPro;
using UnityEngine;

namespace LongLeap.Settings.Controls
{
    public class SettingsDropdown : SettingsDropdownBase
    {
        [SerializeField] private TMP_Text     _label;
        [SerializeField] private TMP_Dropdown _dropdown;

        private IEnumSetting _enumSetting;
        private ISettingsService _service;
        private string _labelKey;

        public override void Bind(ISettingsService service, Setting s)
        {
            _enumSetting = s as IEnumSetting;
            if (_enumSetting == null) return;
            _service = service;
            _labelKey = s.LabelKey;

            _label.text = Loc.Resolve(_labelKey);
            ApplyLocalizedOptions();
            _dropdown.value = _enumSetting.GetOptionIndex();
            _dropdown.onValueChanged.AddListener(OnChanged);
            Loc.OnLanguageChanged += OnLanguageChanged;
        }

        void ApplyLocalizedOptions()
        {
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var l in _enumSetting.GetOptionLabels())
                options.Add(new TMP_Dropdown.OptionData(l));
            _dropdown.options = options;
        }

        void OnLanguageChanged()
        {
            _label.text = Loc.Resolve(_labelKey);
            var index = _enumSetting.GetOptionIndex();
            ApplyLocalizedOptions();
            _dropdown.SetValueWithoutNotify(index);
        }

        void OnChanged(int index) => _enumSetting.ApplyIndex(index, _service);

        void OnDestroy()
        {
            _dropdown.onValueChanged.RemoveListener(OnChanged);
            Loc.OnLanguageChanged -= OnLanguageChanged;
        }
    }
}
