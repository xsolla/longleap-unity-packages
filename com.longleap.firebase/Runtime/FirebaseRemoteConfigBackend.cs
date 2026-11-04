using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.RemoteConfig;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.Firebase
{
    /// <summary>
    /// Fetches Firebase Remote Config parameters and A/B experiment assignments over REST.
    /// </summary>
    /// <remarks>
    /// On first use registers a Firebase Installation ID (FID) and stores it in
    /// <see cref="IPlayerPrefs"/> under <c>rc_fid_data</c>; later fetches reuse it. Parameter
    /// caching is not done here; it belongs to <c>com.longleap.remoteconfig</c>.
    /// </remarks>
    public sealed class FirebaseRemoteConfigBackend : IRemoteConfigProvider
    {
        private readonly FirebaseConfigSO _config;
        private readonly IPlayerPrefs _playerPrefs;
        private readonly Func<string, string, string, Task<string>> _httpHandler;

        private const string FidPrefsKey = "rc_fid_data";

        /// <param name="config">Must have <see cref="FirebaseConfigSO.GoogleAppId"/>, <see cref="FirebaseConfigSO.ProjectNumber"/> and <see cref="FirebaseConfigSO.ApiKey"/> set.</param>
        /// <param name="playerPrefs">Stores the FID between launches.</param>
        /// <exception cref="ArgumentException"><see cref="FirebaseConfigSO.GoogleAppId"/> is empty.</exception>
        public FirebaseRemoteConfigBackend(FirebaseConfigSO config, IPlayerPrefs playerPrefs)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.GoogleAppId))
                throw new ArgumentException("GoogleAppId cannot be empty", nameof(config));
            _playerPrefs = playerPrefs ?? throw new ArgumentNullException(nameof(playerPrefs));
            _httpHandler = SendUnityWebRequestAsync;
        }

        /// <param name="config">Must have <see cref="FirebaseConfigSO.GoogleAppId"/>, <see cref="FirebaseConfigSO.ProjectNumber"/> and <see cref="FirebaseConfigSO.ApiKey"/> set.</param>
        /// <param name="playerPrefs">Stores the FID between launches.</param>
        /// <param name="httpHandler">
        /// Replaces the network call (for tests). Receives method, URL and body; returns the
        /// response body, or <c>null</c> on failure.
        /// </param>
        /// <exception cref="ArgumentException"><see cref="FirebaseConfigSO.GoogleAppId"/> is empty.</exception>
        public FirebaseRemoteConfigBackend(FirebaseConfigSO config, IPlayerPrefs playerPrefs, Func<string, string, string, Task<string>> httpHandler)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.GoogleAppId))
                throw new ArgumentException("GoogleAppId cannot be empty", nameof(config));
            _playerPrefs = playerPrefs ?? throw new ArgumentNullException(nameof(playerPrefs));
            _httpHandler = httpHandler ?? throw new ArgumentNullException(nameof(httpHandler));
        }

        public async Task<RemoteConfigFetchResult> FetchAsync()
        {
            try
            {
                var fidData = LoadFidData();
                if (string.IsNullOrEmpty(fidData?.Fid))
                {
                    var fid = await RegisterFidAsync();
                    if (string.IsNullOrEmpty(fid))
                        return Failed();
                    fidData = new FidData { Fid = fid };
                    SaveFidData(fidData);
                }

                var json = await FetchRemoteConfigAsync(fidData.Fid);
                if (json == null)
                    return Failed();
                return ParseResult(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigBackend)}: FetchAsync failed — {ex.Message}");
                return Failed();
            }
        }

        private FidData LoadFidData()
        {
            var json = _playerPrefs.GetString(FidPrefsKey, null);
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<FidData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigBackend)}: failed to deserialize the cached FID data — {ex.Message}");
                return null;
            }
        }

        private void SaveFidData(FidData data)
        {
            var json = JsonConvert.SerializeObject(data);
            _playerPrefs.SetString(FidPrefsKey, json);
        }

        private async Task<string> RegisterFidAsync()
        {
            var url = $"https://firebaseinstallations.googleapis.com/v1/projects/{_config.ProjectNumber}/installations?key={_config.ApiKey}";
            var fid = Guid.NewGuid().ToString("D");

            var body = new
            {
                fid,
                appId = _config.GoogleAppId,
                authVersion = "FIS_v2",
                sdkVersion = "u:unity"
            };
            var bodyJson = JsonConvert.SerializeObject(body);

            try
            {
                var response = await _httpHandler("POST", url, bodyJson);
                if (response == null)
                {
                    Debug.LogError($"{nameof(FirebaseRemoteConfigBackend)}: failed to register the FID");
                    return null;
                }

                var j = JObject.Parse(response);
                var name = (string)j["name"];
                if (string.IsNullOrEmpty(name))
                {
                    Debug.LogError($"{nameof(FirebaseRemoteConfigBackend)}: the FID registration response has no 'name'");
                    return null;
                }

                var lastSlash = name.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash < name.Length - 1)
                    return name[(lastSlash + 1)..];

                return fid;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigBackend)}: FID registration failed — {ex.Message}");
                return null;
            }
        }

        private async Task<string> FetchRemoteConfigAsync(string fid)
        {
            var url = $"https://firebaseremoteconfig.googleapis.com/v1/projects/{_config.ProjectNumber}/namespaces/firebase:fetch?key={_config.ApiKey}";

            var body = new
            {
                appId = _config.GoogleAppId,
                appInstanceId = fid
            };
            var bodyJson = JsonConvert.SerializeObject(body);

            return await _httpHandler("POST", url, bodyJson);
        }

        private static RemoteConfigFetchResult ParseResult(string json)
        {
            try
            {
                var j = JObject.Parse(json);

                Dictionary<string, string> parameters = null;
                var entries = (JObject)j["entries"];
                if (entries != null)
                {
                    parameters = new Dictionary<string, string>();
                    foreach (var prop in entries.Properties())
                    {
                        parameters[prop.Name] = prop.Value?.ToString();
                    }
                }

                List<AbExperimentInfo> experiments = null;
                var payload = j["experimentPayload"] as JArray;
                if (payload != null && payload.Count > 0)
                {
                    experiments = new List<AbExperimentInfo>();
                    foreach (var item in payload)
                    {
                        var expId = (string)item["experimentId"];
                        var variantStr = (string)item["variantId"];
                        if (!string.IsNullOrEmpty(expId) && int.TryParse(variantStr, out var variantIndex))
                        {
                            experiments.Add(new AbExperimentInfo(expId, variantIndex));
                        }
                    }
                }

                return new RemoteConfigFetchResult(parameters, experiments, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigBackend)}: failed to parse the response — {ex.Message}");
                return Failed();
            }
        }

        private static RemoteConfigFetchResult Failed()
        {
            return new RemoteConfigFetchResult(null, null, false);
        }

        private async Task<string> SendUnityWebRequestAsync(string method, string url, string body)
        {
            using var request = new UnityWebRequest(url, method);

            if (!string.IsNullOrEmpty(body))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            }

            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigBackend)}: HTTP {method} FAILED — {request.error} (code {request.responseCode}), body: {request.downloadHandler.text}");
                return null;
            }

            return request.downloadHandler.text;
        }

        [Serializable]
        private sealed class FidData
        {
            public string Fid;
        }
    }
}
