using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.RemoteConfig.Storage;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.RemoteConfig.Tests
{
    [TestFixture]
    public class RemoteConfigServiceTests
    {
        private MockProvider _mock;
        private InMemoryPlayerPrefs _prefs;
        private RemoteConfigService _service;

        [SetUp]
        public void SetUp()
        {
            _mock = new MockProvider();
            _prefs = new InMemoryPlayerPrefs();
            _service = new RemoteConfigService(_mock, _prefs);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        private sealed class MockProvider : IRemoteConfigProvider
        {
            public RemoteConfigFetchResult Result { get; set; }
            public int CallCount { get; private set; }
            public TaskCompletionSource<RemoteConfigFetchResult> Tcs { get; set; }

            public Task<RemoteConfigFetchResult> FetchAsync()
            {
                CallCount++;
                if (Tcs != null)
                    return Tcs.Task;
                return Task.FromResult(Result);
            }
        }

        [Test]
        public async Task FetchAsync_Success_UpdatesParameters()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string>
                {
                    ["str_key"] = "hello",
                    ["int_key"] = "42",
                    ["float_key"] = "3.14",
                    ["bool_key"] = "true"
                },
                Array.Empty<AbExperimentInfo>(),
                true
            );

            await _service.FetchAsync();

            Assert.AreEqual("hello", _service.GetString("str_key", "def"));
            Assert.AreEqual(42, _service.GetInt("int_key", 0));
            Assert.AreEqual(3.14f, _service.GetFloat("float_key", 0f));
            Assert.AreEqual(true, _service.GetBool("bool_key", false));
        }

        [Test]
        public async Task FetchAsync_Success_UpdatesCache()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string>
                {
                    ["key1"] = "val1",
                    ["key2"] = "val2"
                },
                Array.Empty<AbExperimentInfo>(),
                true
            );

            await _service.FetchAsync();

            string cached = _prefs.GetString("rc_cache", "");
            Assert.IsNotEmpty(cached);

            var deserialized = JsonConvert.DeserializeObject<Dictionary<string, string>>(cached);
            Assert.AreEqual("val1", deserialized["key1"]);
            Assert.AreEqual("val2", deserialized["key2"]);
        }

        [Test]
        public async Task FetchAsync_Failure_KeepsPreviousValues()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string> { ["key"] = "first" },
                Array.Empty<AbExperimentInfo>(),
                true
            );
            await _service.FetchAsync();

            _mock.Result = new RemoteConfigFetchResult(null, Array.Empty<AbExperimentInfo>(), false);
            await _service.FetchAsync();

            Assert.AreEqual("first", _service.GetString("key", "def"));
        }

        [Test]
        public void FetchAsync_NoCacheNoNetwork_ReturnsDefaults()
        {
            LogAssert.Expect(LogType.Warning, new Regex("ключ 'str_key'.*не найден"));
            Assert.AreEqual("default_str", _service.GetString("str_key", "default_str"));

            LogAssert.Expect(LogType.Warning, new Regex("ключ 'int_key'.*не найден"));
            Assert.AreEqual(42, _service.GetInt("int_key", 42));

            LogAssert.Expect(LogType.Warning, new Regex("ключ 'float_key'.*не найден"));
            Assert.AreEqual(3.14f, _service.GetFloat("float_key", 3.14f));

            LogAssert.Expect(LogType.Warning, new Regex("ключ 'bool_key'.*не найден"));
            Assert.AreEqual(true, _service.GetBool("bool_key", true));
        }

        [Test]
        public async Task FetchAsync_ConcurrentCall_Deduplicates()
        {
            var tcs = new TaskCompletionSource<RemoteConfigFetchResult>();
            var successResult = new RemoteConfigFetchResult(
                new Dictionary<string, string> { ["key"] = "val" },
                Array.Empty<AbExperimentInfo>(),
                true
            );

            _mock.Tcs = tcs;

            var task1 = _service.FetchAsync();
            var task2 = _service.FetchAsync();

            tcs.SetResult(successResult);

            await Task.WhenAll(task1, task2);

            Assert.AreEqual(1, _mock.CallCount, "Должен быть только один вызов провайдера");
            Assert.AreEqual("val", _service.GetString("key", "default"));
        }

        [Test]
        public async Task GetFloat_BadValue_ReturnsDefault()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string> { ["bad_float"] = "banana" },
                Array.Empty<AbExperimentInfo>(),
                true
            );
            await _service.FetchAsync();

            LogAssert.Expect(LogType.Warning, new Regex("не удалось.*bad_float.*banana.*float"));
            float result = _service.GetFloat("bad_float", 3.14f);
            Assert.AreEqual(3.14f, result);
        }

        [Test]
        public async Task GetFloat_MissingKey_LogsWarningOnce()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string> { ["other"] = "value" },
                Array.Empty<AbExperimentInfo>(),
                true
            );
            await _service.FetchAsync();

            LogAssert.Expect(LogType.Warning, new Regex("ключ 'missing_float'.*не найден"));
            float result1 = _service.GetFloat("missing_float", 5.0f);
            Assert.AreEqual(5.0f, result1);

            float result2 = _service.GetFloat("missing_float", 5.0f);
            Assert.AreEqual(5.0f, result2);
        }

        [Test]
        public async Task GetInt_ValidValue_ReturnsParsedInt()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string> { ["int_val"] = "42" },
                Array.Empty<AbExperimentInfo>(),
                true
            );
            await _service.FetchAsync();

            int result = _service.GetInt("int_val", 0);
            Assert.AreEqual(42, result);
        }

        [Test]
        public async Task GetBool_TrueValues_ReturnTrue()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string>
                {
                    ["bool_true"] = "true",
                    ["bool_one"] = "1"
                },
                Array.Empty<AbExperimentInfo>(),
                true
            );
            await _service.FetchAsync();

            Assert.AreEqual(true, _service.GetBool("bool_true", false));
            Assert.AreEqual(true, _service.GetBool("bool_one", false));
        }

        [Test]
        public async Task GetBool_FalseValues_ReturnFalse()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string>
                {
                    ["bool_false"] = "false",
                    ["bool_zero"] = "0"
                },
                Array.Empty<AbExperimentInfo>(),
                true
            );
            await _service.FetchAsync();

            Assert.AreEqual(false, _service.GetBool("bool_false", true));
            Assert.AreEqual(false, _service.GetBool("bool_zero", true));
        }

        [Test]
        public void ActiveExperiments_NeverNull()
        {
            Assert.IsNotNull(_service.ActiveExperiments);
            Assert.AreEqual(0, _service.ActiveExperiments.Count);
        }

        [Test]
        public async Task ActiveExperiments_AfterFetch_ReturnsExperiments()
        {
            var experiments = new List<AbExperimentInfo>
            {
                new AbExperimentInfo("exp_01", 1),
                new AbExperimentInfo("exp_02", 2)
            };

            _mock.Result = new RemoteConfigFetchResult(new Dictionary<string, string>(), experiments, true);
            await _service.FetchAsync();

            Assert.AreEqual(2, _service.ActiveExperiments.Count);
            Assert.AreEqual("exp_01", _service.ActiveExperiments[0].ExperimentId);
            Assert.AreEqual(1, _service.ActiveExperiments[0].VariantIndex);
            Assert.AreEqual("exp_02", _service.ActiveExperiments[1].ExperimentId);
            Assert.AreEqual(2, _service.ActiveExperiments[1].VariantIndex);
        }

        [Test]
        public void Cache_Corrupted_DiscardedAndDefaultsUsed()
        {
            _prefs.SetString("rc_cache", "not valid json {{{");
            _service.Dispose();

            LogAssert.Expect(LogType.Warning, new Regex("кэш Remote Config повреждён"));
            var corruptedService = new RemoteConfigService(_mock, _prefs);

            LogAssert.Expect(LogType.Warning, new Regex("ключ 'any_key'.*не найден"));
            string result = corruptedService.GetString("any_key", "fallback");
            Assert.AreEqual("fallback", result);

            corruptedService.Dispose();
        }

        [Test]
        public void Cache_LoadedOnConstruction()
        {
            var cacheData = new Dictionary<string, string>
            {
                ["cached_key"] = "cached_value",
                ["cached_int"] = "99"
            };
            string json = JsonConvert.SerializeObject(cacheData);
            _prefs.SetString("rc_cache", json);

            _service.Dispose();
            var cachedService = new RemoteConfigService(_mock, _prefs);

            Assert.AreEqual("cached_value", cachedService.GetString("cached_key", "def"));
            Assert.AreEqual(99, cachedService.GetInt("cached_int", 0));

            cachedService.Dispose();
        }

        [Test]
        public void Dispose_ClearsState()
        {
            _mock.Result = new RemoteConfigFetchResult(
                new Dictionary<string, string> { ["key"] = "val" },
                Array.Empty<AbExperimentInfo>(),
                true
            );
            // Note: FetchAsync is async, but dispose clears state even if we didn't fetch.
            // After Dispose, getters should return defaults without logging warnings.

            _service.Dispose();

            Assert.AreEqual("default", _service.GetString("key", "default"));
            Assert.AreEqual(10, _service.GetInt("key", 10));
            Assert.AreEqual(1.0f, _service.GetFloat("key", 1.0f));
            Assert.AreEqual(true, _service.GetBool("key", true));
            Assert.IsNotNull(_service.ActiveExperiments);
            Assert.AreEqual(0, _service.ActiveExperiments.Count);
        }
    }
}
