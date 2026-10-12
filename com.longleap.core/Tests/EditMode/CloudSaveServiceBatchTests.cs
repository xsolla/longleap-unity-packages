using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LongLeap.Core.Tests
{
    // Batch pushes and using the service before InitializeAsync.
    //
    // Both are needed for saves on our server: batching so that the first
    // sign-in with hundreds of keys doesn't turn into hundreds of requests, and
    // pre-initialization use so the sign-in screen can snapshot the guest save
    // and apply the player's choice before the cloud is merged into the mirror.
    [TestFixture]
    public class CloudSaveServiceBatchTests
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

        private sealed class FakeBatchProvider : ICloudSaveProvider, ICloudSaveBatchProvider
        {
            public readonly Dictionary<string, CloudSaveEntry> Cloud = new Dictionary<string, CloudSaveEntry>();
            public readonly List<int> BatchSizes = new List<int>();

            public int SingleWrites;
            public Exception BatchThrows;

            public Task SetStringAsync(CloudSaveKey key, string value)
            {
                SingleWrites++;
                var (v, ts) = CloudSaveEnvelope.Parse(value);
                Cloud[key.Value] = new CloudSaveEntry(v, ts);
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(CloudSaveKey key) =>
                Task.FromResult(Cloud.TryGetValue(key.Value, out var e)
                    ? CloudSaveEnvelope.Serialize(e.Value, e.TimestampUnixSeconds)
                    : null);

            public Task<bool> HasKeyAsync(CloudSaveKey key) => Task.FromResult(Cloud.ContainsKey(key.Value));

            public Task DeleteKeyAsync(CloudSaveKey key)
            {
                SingleWrites++;
                Cloud.Remove(key.Value);
                return Task.CompletedTask;
            }

            public Task<Dictionary<string, string>> LoadAllAsync()
            {
                var all = new Dictionary<string, string>();

                foreach (var pair in Cloud)
                    all[pair.Key] = CloudSaveEnvelope.Serialize(pair.Value.Value, pair.Value.TimestampUnixSeconds);

                return Task.FromResult(all);
            }

            public Task DeleteAllAsync()
            {
                Cloud.Clear();
                return Task.CompletedTask;
            }

            public Task SetManyAsync(
                IReadOnlyDictionary<string, CloudSaveEntry> entries,
                IReadOnlyCollection<string> deletes)
            {
                if (BatchThrows != null)
                    throw BatchThrows;

                BatchSizes.Add(entries.Count + deletes.Count);

                foreach (var pair in entries)
                    Cloud[pair.Key] = pair.Value;

                foreach (var key in deletes)
                    Cloud.Remove(key);

                return Task.CompletedTask;
            }
        }

        private FakePlayerPrefs _prefs;
        private FakeBatchProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _prefs = new FakePlayerPrefs();
            _provider = new FakeBatchProvider();
        }

        private CloudSaveService NewService() => new CloudSaveService(_provider, _prefs);

        // ---- batch push ----

        [Test]
        public async Task BatchProvider_WritesGoOutAsOneBatch()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();

                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");
                await service.SetStringAsync(new CloudSaveKey("heroes_owned"), "[\"monk\"]");
                await service.SetStringAsync(new CloudSaveKey("stat_kill:total"), "7");

                // A write doesn't wait for the network: until someone forces
                // the push, the cloud is empty.
                Assert.AreEqual(0, _provider.BatchSizes.Count);

                await service.FlushPendingAsync();

                CollectionAssert.AreEqual(new[] { 3 }, _provider.BatchSizes);
                Assert.AreEqual(0, _provider.SingleWrites, "a batch provider needs no per-key calls");
                Assert.AreEqual("250", _provider.Cloud["wallet"].Value);
                Assert.AreEqual("[]", _prefs.GetString("cloudsave_dirty", null));
            }
        }

        [Test]
        public async Task BatchProvider_DeletesGoInTheSameBatch()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");
                await service.FlushPendingAsync();

                await service.DeleteKeyAsync(new CloudSaveKey("wallet"));
                await service.SetStringAsync(new CloudSaveKey("heroes_owned"), "[\"monk\"]");
                await service.FlushPendingAsync();

                Assert.IsFalse(_provider.Cloud.ContainsKey("wallet"));
                Assert.IsTrue(_provider.Cloud.ContainsKey("heroes_owned"));
                CollectionAssert.AreEqual(new[] { 1, 2 }, _provider.BatchSizes);
            }
        }

        // The batch didn't go through — keys must stay dirty and go out later.
        [Test]
        public async Task BatchProvider_Failure_KeepsKeysDirty()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();

                _provider.BatchThrows = new InvalidOperationException("network");
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("batch push failed"));
                await service.FlushPendingAsync();

                StringAssert.Contains("wallet", _prefs.GetString("cloudsave_dirty", null));

                _provider.BatchThrows = null;
                await service.FlushPendingAsync();

                Assert.AreEqual("250", _provider.Cloud["wallet"].Value);
                Assert.AreEqual("[]", _prefs.GetString("cloudsave_dirty", null));
            }
        }

        // ---- before InitializeAsync ----

        // A guest save lives only locally: the cloud won't load it at startup,
        // so the snapshot has to take keys from the index.
        [Test]
        public async Task Snapshot_BeforeInitialize_SeesEveryIndexedKey()
        {
            using (CloudSaveService first = NewService())
            {
                await first.InitializeAsync();
                await first.SetStringAsync(new CloudSaveKey("wallet"), "250");
                await first.SetStringAsync(new CloudSaveKey("heroes_owned"), "[\"monk\"]");
                await first.FlushPendingAsync();
            }

            using (CloudSaveService second = NewService())
            {
                IReadOnlyDictionary<string, CloudSaveEntry> snapshot = await second.SnapshotAsync();

                Assert.AreEqual("250", snapshot["wallet"].Value);
                Assert.AreEqual("[\"monk\"]", snapshot["heroes_owned"].Value);
            }
        }

        // A write before initialization must not overwrite the index with its
        // single key: the other keys would be lost for clearing and snapshots.
        [Test]
        public async Task Write_BeforeInitialize_KeepsTheIndex()
        {
            using (CloudSaveService first = NewService())
            {
                await first.InitializeAsync();
                await first.SetStringAsync(new CloudSaveKey("wallet"), "250");
                await first.FlushPendingAsync();
            }

            using (CloudSaveService second = NewService())
            {
                await second.SetStringAsync(new CloudSaveKey("heroes_owned"), "[\"monk\"]");
                await second.FlushPendingAsync();
            }

            string index = _prefs.GetString("cloudsave_index", null);

            StringAssert.Contains("wallet", index);
            StringAssert.Contains("heroes_owned", index);
        }
    }
}
