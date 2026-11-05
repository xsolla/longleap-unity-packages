using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using UnityEngine;

namespace LongLeap.Settings
{
    public sealed class SettingsService : ISettingsService
    {
        private readonly ICloudSaveService _cloudSave;
        private readonly List<ISettingsGroup> _groups = new();
        private readonly HashSet<string> _storageKeys = new();

        public IReadOnlyList<ISettingsGroup> Groups => _groups;
        public event Action<Setting> OnChanged;

        public SettingsService(ICloudSaveService cloudSave) => _cloudSave = cloudSave;

        public void Register(ISettingsGroup group)
        {
            if (!_storageKeys.Add(group.StorageKey))
                throw new InvalidOperationException(
                    $"SettingsService: duplicate StorageKey '{group.StorageKey}'");
            _groups.Add(group);
        }

        public void SetValue<T>(Setting<T> setting, T value)
        {
            value = setting.Coerce(value);
            if (EqualityComparer<T>.Default.Equals(setting.Value, value))
                return;
            setting.Value = value;
            OnChanged?.Invoke(setting);
        }

        public async Task LoadAllAsync()
        {
            if (_cloudSave == null) return;

            foreach (var group in _groups)
            {
                try
                {
                    string json = await _cloudSave.GetStringAsync(new CloudSaveKey(group.StorageKey));
                    if (!string.IsNullOrEmpty(json) && json != "{}")
                        group.Deserialize(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"SettingsService: failed to load '{group.StorageKey}', using default values. {e.Message}");
                }
            }
        }

        public async Task SaveAllAsync()
        {
            if (_cloudSave == null) return;

            foreach (var group in _groups)
            {
                string json = group.Serialize();
                await _cloudSave.SetStringAsync(new CloudSaveKey(group.StorageKey), json);
            }
        }
    }
}
