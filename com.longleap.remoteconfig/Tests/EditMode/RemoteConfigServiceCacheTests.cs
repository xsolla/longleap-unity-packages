using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LongLeap.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.RemoteConfig.Tests
{
    // The PlayerPrefs cache: what is written and what the next launch reads.
    [TestFixture]
    public sealed class RemoteConfigServiceCacheTests
    {
        private FakeRemoteConfigProvider _provider;
        private InMemoryPlayerPrefs _prefs;

        [SetUp]
        public void SetUp()
        {
            _provider = new FakeRemoteConfigProvider();
            _prefs = new InMemoryPlayerPrefs();
        }

        [Test]
        public async Task Fetch_Success_CachesParametersAndExperimentsForTheNextLaunch()
        {
            var first = new RemoteConfigService(_provider, _prefs);
            _provider.Result = FakeRemoteConfigProvider.Success(
                new[] { ("cached_key", "cached_value"), ("cached_int", "99") },
                new[] { new AbExperimentInfo("exp_01", 2) });
            await first.FetchAsync();
            first.Dispose();

            var json = JObject.Parse(_prefs.GetString(RemoteConfigOptions.DefaultCacheKey, ""));
            Assert.AreEqual(1, (int)json["v"]);
            Assert.AreEqual("cached_value", (string)json["parameters"]["cached_key"]);

            var next = new RemoteConfigService(new NullRemoteConfigProvider(), _prefs);
            Assert.AreEqual("cached_value", next.GetString("cached_key", "def"));
            Assert.AreEqual(99, next.GetInt("cached_int", 0));
            Assert.AreEqual(1, next.ActiveExperiments.Count, "experiments are cached with the parameters");
            Assert.AreEqual("exp_01", next.ActiveExperiments[0].ExperimentId);
            Assert.AreEqual(2, next.ActiveExperiments[0].VariantIndex);
            Assert.IsNotNull(next.LastSuccessfulFetchUtc);
            Assert.IsNull(next.LastFetchStatus, "no request was made in this session");
        }

        [TestCase("not valid json {{{", "corrupted")]
        [TestCase("{\"v\":99,\"parameters\":{}}", "format is unknown")]
        [TestCase("{\"legacy_key\":\"value\"}", "format is unknown")]
        [TestCase("{\"v\":1,\"fetchedAtUnixMs\":99999999999999999,\"parameters\":{}}", "invalid fetch time")]
        public void Cache_Unreadable_IsDiscardedWithAWarning(string cached, string reason)
        {
            _prefs.SetString(RemoteConfigOptions.DefaultCacheKey, cached);

            LogAssert.Expect(LogType.Warning, new Regex($"RemoteConfigService: .*{reason}.*discarded"));
            var service = new RemoteConfigService(_provider, _prefs);

            Assert.AreEqual(0, service.Values.Count);
            Assert.AreEqual("fallback", service.GetString("legacy_key", "fallback"));
            LogAssert.NoUnexpectedReceived();
        }

        private sealed class FullPlayerPrefs : IPlayerPrefs
        {
            public string GetString(string key, string defaultValue = "") => defaultValue;
            public int GetInt(string key, int defaultValue = 0) => defaultValue;
            public float GetFloat(string key, float defaultValue = 0f) => defaultValue;
            public void SetString(string key, string value) => throw new PlayerPrefsException("quota exceeded");
            public void SetInt(string key, int value) => throw new PlayerPrefsException("quota exceeded");
            public void SetFloat(string key, float value) => throw new PlayerPrefsException("quota exceeded");
            public bool HasKey(string key) => false;
            public void DeleteKey(string key) { }
            public void Save() { }
        }

        [Test]
        public async Task CacheWriteFails_FetchStillSucceedsAndApplies()
        {
            var service = new RemoteConfigService(_provider, new FullPlayerPrefs());
            _provider.Result = FakeRemoteConfigProvider.Success(("key", "fresh"));

            LogAssert.Expect(LogType.Warning, new Regex("RemoteConfigService: the cache could not be saved: quota exceeded"));
            var status = await service.FetchAsync();

            Assert.AreEqual(RemoteConfigFetchStatus.Success, status);
            Assert.AreEqual(RemoteConfigFetchStatus.Success, service.LastFetchStatus);
            Assert.AreEqual("fresh", service.GetString("key", "def"));
        }

        [Test]
        public async Task CacheKey_SeparatesEnvironments()
        {
            var staging = new RemoteConfigService(_provider, _prefs, new RemoteConfigOptions { CacheKey = "rc_cache_staging" });
            _provider.Result = FakeRemoteConfigProvider.Success(("key", "staging"));
            await staging.FetchAsync();

            Assert.IsTrue(_prefs.HasKey("rc_cache_staging"));
            Assert.IsFalse(_prefs.HasKey(RemoteConfigOptions.DefaultCacheKey));
            Assert.IsFalse(new RemoteConfigService(_provider, _prefs).HasKey("key"));
        }
    }
}
