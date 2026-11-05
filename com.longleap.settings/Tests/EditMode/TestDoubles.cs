using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using UnityEngine.InputSystem;

namespace LongLeap.Settings.Tests
{
    /// <summary>In-memory cloud save. Every call completes synchronously.</summary>
    internal sealed class FakeCloudSave : ICloudSaveService
    {
        public readonly Dictionary<string, string> Data = new();
        public readonly List<string> Writes = new();

        /// <summary>Keys whose read or write throws.</summary>
        public readonly HashSet<string> Failing = new();

        public Task InitializeAsync() => Task.CompletedTask;

        public Task SetStringAsync(CloudSaveKey key, string value)
        {
            if (Failing.Contains(key.Value)) throw new InvalidOperationException("write failed");
            Data[key.Value] = value;
            Writes.Add(key.Value);
            return Task.CompletedTask;
        }

        public Task<string> GetStringAsync(CloudSaveKey key)
        {
            if (Failing.Contains(key.Value)) throw new InvalidOperationException("read failed");
            return Task.FromResult(Data.TryGetValue(key.Value, out var v) ? v : null);
        }

        public Task<bool> HasKeyAsync(CloudSaveKey key) => Task.FromResult(Data.ContainsKey(key.Value));

        public Task DeleteKeyAsync(CloudSaveKey key)
        {
            Data.Remove(key.Value);
            return Task.CompletedTask;
        }

        public Task DeleteAllAsync()
        {
            Data.Clear();
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    public enum TestQuality { Low, Medium, High }

    /// <summary>
    /// Group with one setting of every kind. Serializes as "volume|count|flag|quality",
    /// so tests do not depend on a JSON library.
    /// </summary>
    internal sealed class TestGroup : ISettingsGroup
    {
        private readonly string _storageKey;

        public TestGroup(string storageKey = "settings.test") => _storageKey = storageKey;

        public string StorageKey => _storageKey;
        public string DisplayNameKey => "settings_test";

        public NumericSetting<float> Volume { get; } = new("test.volume", 0.5f, 0f, 1f, "test_volume");
        public NumericSetting<int> Count { get; } = new("test.count", 10, 0, 100, "test_count");
        public Setting<bool> Flag { get; } = new("test.flag", true, "test_flag");
        public EnumSetting<TestQuality> Quality { get; } = new("test.quality", TestQuality.Medium, "test_quality");

        public Setting[] All => new Setting[] { Volume, Count, Flag, Quality };

        public string Serialize() => string.Join("|",
            Volume.Value.ToString(CultureInfo.InvariantCulture),
            Count.Value.ToString(CultureInfo.InvariantCulture),
            Flag.Value.ToString(),
            Quality.Value.ToString());

        public void Deserialize(string data)
        {
            var parts = data.Split('|');
            Volume.Value = float.Parse(parts[0], CultureInfo.InvariantCulture);
            Count.Value = int.Parse(parts[1], CultureInfo.InvariantCulture);
            Flag.Value = bool.Parse(parts[2]);
            Quality.Value = Enum.Parse<TestQuality>(parts[3]);
        }
    }

    /// <summary>Two key bindings, for rebind tests.</summary>
    internal sealed class TestKeysGroup : ISettingsGroup
    {
        private readonly string _storageKey;

        public TestKeysGroup(string storageKey = "settings.test_keys", Key jump = Key.Space, Key fire = Key.F)
        {
            _storageKey = storageKey;
            Jump = new Setting<Key>(storageKey + ".jump", jump, "key_jump");
            Fire = new Setting<Key>(storageKey + ".fire", fire, "key_fire");
        }

        public string StorageKey => _storageKey;
        public string DisplayNameKey => "settings_test_keys";

        public Setting<Key> Jump { get; }
        public Setting<Key> Fire { get; }

        public Setting[] All => new Setting[] { Jump, Fire };

        public string Serialize() => "";
        public void Deserialize(string data) { }
    }
}
