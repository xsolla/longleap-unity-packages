using System;
using System.Collections.Generic;
using LongLeap.Localization;

namespace LongLeap.Settings
{
    public interface IEnumSetting
    {
        string[] GetOptionLabels();
        int      GetOptionIndex();
        void     ApplyIndex(int index, ISettingsService service);
    }

    public abstract class Setting
    {
        public string Key { get; }
        public string LabelKey { get; }
        public abstract Type ValueType { get; }
        public abstract object BoxedValue { get; }

        protected Setting(string key, string labelKey)
        {
            Key = key;
            LabelKey = labelKey;
        }

        /// <summary>
        /// Restore the default value through the service, so <see cref="ISettingsService.OnChanged"/> fires.
        /// An untyped setting has no default; only <see cref="Setting{T}"/> knows it.
        /// </summary>
        internal virtual void ResetToDefault(ISettingsService service) { }
    }

    public class Setting<T> : Setting
    {
        public T DefaultValue { get; }
        public T Value { get; set; }
        public override object BoxedValue => Value;
        public override Type ValueType => typeof(T);

        public Setting(string key, T defaultValue, string labelKey) : base(key, labelKey)
        {
            DefaultValue = defaultValue;
            Value = defaultValue;
        }

        internal virtual T Coerce(T value) => value;

        internal override void ResetToDefault(ISettingsService service)
            => service.SetValue(this, DefaultValue);
    }

    public class NumericSetting<T> : Setting<T> where T : struct
    {
        public T Min { get; }
        public T Max { get; }

        public NumericSetting(string key, T defaultValue, T min, T max, string labelKey)
            : base(key, defaultValue, labelKey)
        {
            Min = min;
            Max = max;
        }

        internal override T Coerce(T value)
        {
            var c = Comparer<T>.Default;
            if (c.Compare(value, Min) < 0) return Min;
            if (c.Compare(value, Max) > 0) return Max;
            return value;
        }
    }

    public class EnumSetting<T> : Setting<T>, IEnumSetting where T : struct, Enum
    {
        public EnumSetting(string key, T defaultValue, string labelKey)
            : base(key, defaultValue, labelKey) { }

        string[] IEnumSetting.GetOptionLabels()
        {
            var names = Enum.GetNames(typeof(T));
            var labels = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
                labels[i] = Loc.Resolve($"{LabelKey}_{names[i].ToLowerInvariant()}");
            return labels;
        }

        int IEnumSetting.GetOptionIndex()
        {
            var values = (T[])Enum.GetValues(typeof(T));
            for (int i = 0; i < values.Length; i++)
                if (EqualityComparer<T>.Default.Equals(values[i], Value)) return i;
            return 0;
        }

        void IEnumSetting.ApplyIndex(int index, ISettingsService service)
        {
            var v = ((T[])Enum.GetValues(typeof(T)))[index];
            service.SetValue(this, v);
        }
    }
}
