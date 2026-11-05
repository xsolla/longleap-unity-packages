using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;

namespace LongLeap.Core.Tests
{
    // Key index and local mirror reset.
    //
    // Both are needed for an identity switch: without the index the mirror
    // cannot be cleared completely (PlayerPrefs cannot be enumerated), and a
    // forgotten key with a newer timestamp would beat the cloud value on the
    // next load — leaking one identity's progress into another.
    [TestFixture]
    public class CloudSaveServiceLocalResetTests
    {
        private sealed class FakePlayerPrefs : IPlayerPrefs
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();

            public string GetString(string key, string defaultValue = "") =>
                Values.TryGetValue(key, out string v) ? v : defaultValue;

            public int GetInt(string key, int defaultValue = 0) => defaultValue;
            public float GetFloat(string key, float defaultValue = 0f) => defaultValue;
            public void SetString(string key, string value) => Values[key] = value;
            public void SetInt(string key, int value) { }
            public void SetFloat(string key, float value) { }
            public bool HasKey(string key) => Values.ContainsKey(key);
            public void DeleteKey(string key) => Values.Remove(key);
            public void Save() { }
        }

        private sealed class FakeProvider : ICloudSaveProvider
        {
            public readonly Dictionary<string, string> Cloud = new Dictionary<string, string>();

            public int DeleteAllCalls;
            public int DeleteKeyCalls;

            public Task SetStringAsync(CloudSaveKey key, string value)
            {
                Cloud[key.Value] = value;
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(CloudSaveKey key) =>
                Task.FromResult(Cloud.TryGetValue(key.Value, out string v) ? v : null);

            public Task<bool> HasKeyAsync(CloudSaveKey key) => Task.FromResult(Cloud.ContainsKey(key.Value));

            public Task DeleteKeyAsync(CloudSaveKey key)
            {
                DeleteKeyCalls++;
                Cloud.Remove(key.Value);
                return Task.CompletedTask;
            }

            public Task<Dictionary<string, string>> LoadAllAsync() =>
                Task.FromResult(new Dictionary<string, string>(Cloud));

            public Task DeleteAllAsync()
            {
                DeleteAllCalls++;
                Cloud.Clear();
                return Task.CompletedTask;
            }
        }

        private FakePlayerPrefs _prefs;
        private FakeProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _prefs = new FakePlayerPrefs();
            _provider = new FakeProvider();
        }

        private CloudSaveService NewService() => new CloudSaveService(_provider, _prefs);

        [Test]
        public async Task ResetLocalCache_ClearsMirrorOfKeysWrittenThisSession()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                Assert.AreEqual("250", _prefs.GetString("cloudsave_wallet", null));

                await service.ResetLocalCacheAsync();

                Assert.IsNull(_prefs.GetString("cloudsave_wallet", null));
                Assert.IsNull(_prefs.GetString("cloudsave_ts_wallet", null));
            }
        }

        // The reason the index exists: a key nobody read or wrote in THIS
        // session is not in the cache, yet it must still leave the mirror.
        [Test]
        public async Task ResetLocalCache_ClearsKeysNobodyTouchedThisSession()
        {
            using (CloudSaveService first = NewService())
            {
                await first.InitializeAsync();
                await first.SetStringAsync(new CloudSaveKey("wallet"), "250");
                await first.SetStringAsync(new CloudSaveKey("heroes_owned"), "[\"monk\"]");
            }

            // The new service reads no keys — it only loads the index.
            using (CloudSaveService second = NewService())
            {
                await second.InitializeAsync();
                await second.ResetLocalCacheAsync();
            }

            Assert.IsNull(_prefs.GetString("cloudsave_wallet", null));
            Assert.IsNull(_prefs.GetString("cloudsave_heroes_owned", null));
            Assert.IsNull(_prefs.GetString("cloudsave_ts_heroes_owned", null));
            Assert.IsNull(_prefs.GetString("cloudsave_index", null));
            Assert.IsNull(_prefs.GetString("cloudsave_dirty", null));
        }

        // The reset is LOCAL. Touching the cloud would take away the owner's
        // save, which is exactly what must stay untouched.
        [Test]
        public async Task ResetLocalCache_DoesNotTouchProvider()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                await service.ResetLocalCacheAsync();

                Assert.AreEqual(0, _provider.DeleteAllCalls);
                Assert.AreEqual(0, _provider.DeleteKeyCalls);
                Assert.AreEqual("250", CloudSaveEnvelope.Parse(_provider.Cloud["wallet"]).value);
            }
        }

        // After a reset the service is not initialized again: the next load
        // must bring everything back from the new identity's save.
        [Test]
        public async Task ResetLocalCache_ThenInitialize_RestoresFromCloud()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                await service.ResetLocalCacheAsync();
                await service.InitializeAsync();

                Assert.AreEqual("250", await service.GetStringAsync(new CloudSaveKey("wallet")));
            }
        }

        [Test]
        public async Task DeleteAll_AlsoClearsTheIndex()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                await service.DeleteAllAsync();

                Assert.IsNull(_prefs.GetString("cloudsave_index", null));
                Assert.AreEqual(1, _provider.DeleteAllCalls, "DeleteAll must reach the cloud");
            }
        }

        // The index must survive a restart: otherwise the next session would
        // know only the keys it happened to touch, and the index would be useless.
        [Test]
        public async Task Index_SurvivesServiceRestart()
        {
            using (CloudSaveService first = NewService())
            {
                await first.InitializeAsync();
                await first.SetStringAsync(new CloudSaveKey("wallet"), "250");
            }

            string index = _prefs.GetString("cloudsave_index", null);

            Assert.IsNotNull(index);
            Assert.IsTrue(index.Contains("wallet"), "the index must contain the written key");
        }
    }
}
