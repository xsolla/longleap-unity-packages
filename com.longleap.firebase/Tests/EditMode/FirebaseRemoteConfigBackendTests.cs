using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.RemoteConfig.Storage;
using LongLeap.Firebase;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Firebase.Tests
{
    [TestFixture]
    public class FirebaseRemoteConfigBackendTests
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
            var backend = new FirebaseRemoteConfigBackend(config, _prefs, handler);
            var result = await backend.FetchAsync();

            Assert.IsTrue(result.IsSuccess, "FetchAsync должен вернуть успешный результат");
            Assert.AreEqual(2, callLog.Count, "Должно быть 2 HTTP-вызова: регистрация FID, RC fetch");

            Assert.IsTrue(callLog[0].url.Contains("/installations"));

            Assert.IsTrue(callLog[1].url.Contains(":fetch"));

            Assert.AreEqual("value1", result.Parameters["key1"]);
            Assert.AreEqual("value2", result.Parameters["key2"]);
            Assert.IsNotNull(result.Experiments);
            Assert.AreEqual(1, result.Experiments.Count);
            Assert.AreEqual("exp_test", result.Experiments[0].ExperimentId);
            Assert.AreEqual(1, result.Experiments[0].VariantIndex);

            Assert.IsTrue(_prefs.HasKey(FidPrefsKey), "FID данные должны быть сохранены");
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
            var backend = new FirebaseRemoteConfigBackend(config, _prefs, handler);
            var result = await backend.FetchAsync();

            Assert.IsTrue(result.IsSuccess, "FetchAsync должен вернуть успешный результат");
            Assert.AreEqual(1, callLog.Count, "Должен быть ровно 1 HTTP-вызов (только RC fetch)");
            Assert.IsTrue(callLog[0].Contains(":fetch"), "Единственный вызов должен быть RC fetch");
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
            var backend = new FirebaseRemoteConfigBackend(config, _prefs, handler);
            var result = await backend.FetchAsync();

            Assert.IsFalse(result.IsSuccess, "Сетевая ошибка должна вернуть IsSuccess=false");
        }

        [Test]
        public void Constructor_EmptyGoogleAppId_ThrowsArgumentException()
        {
            var config = CreateConfigWithOverrides(googleAppId: "");

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                _ = new FirebaseRemoteConfigBackend(config, _prefs);
            });

            Assert.That(ex.Message, Does.Contain("GoogleAppId"));
        }
    }
}
