using System.Collections.Generic;
using System.Globalization;
using LongLeap.Core;

namespace LongLeap.RemoteConfig.Storage
{
    public sealed class InMemoryPlayerPrefs : IPlayerPrefs
    {
        private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

        public string GetString(string key, string defaultValue = "")
        {
            return _store.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (_store.TryGetValue(key, out var value) && int.TryParse(value, out var result))
                return result;
            return defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (_store.TryGetValue(key, out var value)
                && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;
            return defaultValue;
        }

        public void SetString(string key, string value)
        {
            _store[key] = value;
        }

        public void SetInt(string key, int value)
        {
            _store[key] = value.ToString();
        }

        public void SetFloat(string key, float value)
        {
            _store[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        public bool HasKey(string key)
        {
            return _store.ContainsKey(key);
        }

        public void DeleteKey(string key)
        {
            _store.Remove(key);
        }

        public void Save()
        {
        }
    }
}
