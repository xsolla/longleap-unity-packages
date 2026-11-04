using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.Firebase;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Firebase.Tests
{
    [TestFixture]
    public class FirebaseRemoteConfigProviderTests
    {
        private const string TestApiKey = "test-api-key";
        private const long TestProjectNumber = 123456789L;
        private const string TestGoogleAppId = "1:123456789:android:abcdef123456";
        private const string TestFid = "test-fid-uuid-12345";
        private const string FidPrefsKey = "rc_fid_data";

        private InMemoryPlayerPrefs _prefs;

        [SetUp]
        public void SetUp()
        {
            _prefs = new InMemoryPlayerPrefs();
        }

        private static FirebaseConfigSO CreateConfigWithOverrides(string googleAppId = TestGoogleAppId, long projectNumber = TestProjectNumber, string apiKey = TestApiKey)
        {
            var config = ScriptableObject.CreateInstance<FirebaseConfigSO>();
            var so = new SerializedObject(config);
            so.FindProperty("_googleAppId").stringValue = googleAppId;
            so.FindProperty("_projectNumber").longValue = projectNumber;
            so.FindProperty("_apiKey").stringValue = apiKey;
            so.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static string BuildFidDataJson(string fid)
        {
            var data = new Dictionary<string, object>
            {
                ["fid"] = fid
            };
            return JsonConvert.SerializeObject(data);
        }

        private void SaveValidFidData()
        {
            _prefs.SetString(FidPrefsKey, BuildFidDataJson(TestFid));
        }

        [Test]
        public async Task FetchAsync_NoFid_RegistersAndFetches()
        {
            var callLog = new List<(string method, string url, string body)>();

            Func<string, string, string, Task<string>> handler = (method, url, body) =>
            {
                callLog.Add((method, url, body));

                if (url.Contains("/installations"))
                {
                    return Task.FromResult(@"{""name"": ""projects/123456789/installations/" + TestFid + @"""}");
                }

                if (url.Contains(":fetch"))
                {
                    return Task.FromResult(@"{""entries"": {""key1"": ""value1"", ""key2"": ""value2""}, ""experimentPayload"": [{""experimentId"": ""exp_test"", ""variantId"": ""1""}]}");
                }

                return Task.FromResult<string>(null);
            };

            var config = CreateConfigWithOverrides();
            var provider = new FirebaseRemoteConfigProvider(config, _prefs, handler);
            var result = await provider.FetchAsync();

            Assert.IsTrue(result.IsSuccess, "FetchAsync should succeed");
            Assert.AreEqual(2, callLog.Count, "Expected 2 HTTP calls: FID registration, RC fetch");

            Assert.IsTrue(callLog[0].url.Contains("/installations"));

            Assert.IsTrue(callLog[1].url.Contains(":fetch"));

            Assert.AreEqual("value1", result.Parameters["key1"]);
            Assert.AreEqual("value2", result.Parameters["key2"]);
            Assert.IsNotNull(result.Experiments);
            Assert.AreEqual(1, result.Experiments.Count);
            Assert.AreEqual("exp_test", result.Experiments[0].ExperimentId);
            Assert.AreEqual(1, result.Experiments[0].VariantIndex);

            Assert.IsTrue(_prefs.HasKey(FidPrefsKey), "FID data should be saved");
        }

        [Test]
        public async Task FetchAsync_ValidFid_UsesCachedFid()
        {
            SaveValidFidData();
            var callLog = new List<string>();

            Func<string, string, string, Task<string>> handler = (method, url, body) =>
            {
                callLog.Add(url);
                if (url.Contains(":fetch"))
                {
                    return Task.FromResult(@"{""entries"": {""p1"": ""v1""}, ""experimentPayload"": []}");
                }
                return Task.FromResult<string>(null);
            };

            var config = CreateConfigWithOverrides();
            var provider = new FirebaseRemoteConfigProvider(config, _prefs, handler);
            var result = await provider.FetchAsync();

            Assert.IsTrue(result.IsSuccess, "FetchAsync should succeed");
            Assert.AreEqual(1, callLog.Count, "Expected exactly 1 HTTP call (RC fetch only)");
            Assert.IsTrue(callLog[0].Contains(":fetch"), "The only call should be the RC fetch");
            Assert.AreEqual("v1", result.Parameters["p1"]);
        }

        [Test]
        public async Task FetchAsync_NetworkError_ReturnsFailure()
        {
            var callLog = new List<string>();

            Func<string, string, string, Task<string>> handler = (method, url, body) =>
            {
                callLog.Add(url);
                if (url.Contains(":fetch"))
                {
                    return Task.FromResult<string>(null);
                }
                if (url.Contains("/installations"))
                {
                    return Task.FromResult(@"{""name"": ""projects/123456789/installations/" + TestFid + @"""}");
                }
                return Task.FromResult<string>(null);
            };

            var config = CreateConfigWithOverrides();
            var provider = new FirebaseRemoteConfigProvider(config, _prefs, handler);
            var result = await provider.FetchAsync();

            Assert.IsFalse(result.IsSuccess, "A network error should return IsSuccess=false");
        }

        [Test]
        public async Task FetchAsync_DateLikeValues_StayVerbatim()
        {
            SaveValidFidData();
            Func<string, string, string, Task<string>> handler = (method, url, body) =>
                Task.FromResult(@"{""entries"": {""sale_end"": ""2026-12-01T00:00:00Z"", ""day"": ""2026-12-01""}}");

            var provider = new FirebaseRemoteConfigProvider(CreateConfigWithOverrides(), _prefs, handler);
            var result = await provider.FetchAsync();

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("2026-12-01T00:00:00Z", result.Parameters["sale_end"]);
            Assert.AreEqual("2026-12-01", result.Parameters["day"]);
        }

        [Test]
        public async Task FetchAsync_NoEntries_SucceedsWithNoParameters()
        {
            SaveValidFidData();
            Func<string, string, string, Task<string>> handler = (method, url, body) =>
                Task.FromResult(@"{""state"": ""EMPTY_CONFIG""}");

            var provider = new FirebaseRemoteConfigProvider(CreateConfigWithOverrides(), _prefs, handler);
            var result = await provider.FetchAsync();

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, result.Parameters.Count);
            Assert.AreEqual(0, result.Experiments.Count);
        }

        [Test]
        public async Task FetchAsync_CanceledDuringRequest_DropsTheAnswerAndThrows()
        {
            SaveValidFidData();
            var cts = new CancellationTokenSource();
            Func<string, string, string, Task<string>> handler = (method, url, body) =>
            {
                cts.Cancel();
                return Task.FromResult(@"{""entries"": {""p1"": ""v1""}}");
            };

            var provider = new FirebaseRemoteConfigProvider(CreateConfigWithOverrides(), _prefs, handler);

            // Awaited rather than Assert.CatchAsync: a blocking wait on a continuation posted to
            // Unity's synchronization context would never finish.
            OperationCanceledException caught = null;
            try
            {
                await provider.FetchAsync(cts.Token);
            }
            catch (OperationCanceledException ex)
            {
                caught = ex;
            }

            Assert.IsNotNull(caught, "cancellation must surface as OperationCanceledException");
        }

        [Test]
        public void Constructor_EmptyGoogleAppId_ThrowsArgumentException()
        {
            var config = CreateConfigWithOverrides(googleAppId: "");

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                _ = new FirebaseRemoteConfigProvider(config, _prefs);
            });

            Assert.That(ex.Message, Does.Contain("GoogleAppId"));
        }
    }
}
