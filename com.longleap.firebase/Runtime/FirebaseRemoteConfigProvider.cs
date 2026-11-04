using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
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
    public sealed class FirebaseRemoteConfigProvider : IRemoteConfigProvider
    {
        private readonly FirebaseConfigSO _config;
        private readonly IPlayerPrefs _playerPrefs;
        // null: real requests through UnityWebRequest, which can be aborted on cancellation.
        private readonly Func<string, string, string, Task<string>> _httpHandler;

        private const string FidPrefsKey = "rc_fid_data";

        /// <param name="config">Must have <see cref="FirebaseConfigSO.GoogleAppId"/>, <see cref="FirebaseConfigSO.ProjectNumber"/> and <see cref="FirebaseConfigSO.ApiKey"/> set.</param>
        /// <param name="playerPrefs">Stores the FID between launches.</param>
        /// <exception cref="ArgumentException"><see cref="FirebaseConfigSO.GoogleAppId"/> is empty.</exception>
        public FirebaseRemoteConfigProvider(FirebaseConfigSO config, IPlayerPrefs playerPrefs)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.GoogleAppId))
                throw new ArgumentException("GoogleAppId cannot be empty", nameof(config));
            _playerPrefs = playerPrefs ?? throw new ArgumentNullException(nameof(playerPrefs));
            _httpHandler = null;
        }

        /// <param name="config">Must have <see cref="FirebaseConfigSO.GoogleAppId"/>, <see cref="FirebaseConfigSO.ProjectNumber"/> and <see cref="FirebaseConfigSO.ApiKey"/> set.</param>
        /// <param name="playerPrefs">Stores the FID between launches.</param>
        /// <param name="httpHandler">
        /// Replaces the network call (for tests). Receives method, URL and body; returns the
        /// response body, or <c>null</c> on failure. It is not given the cancellation token; an
        /// answer that arrives after cancellation is dropped.
        /// </param>
        /// <exception cref="ArgumentException"><see cref="FirebaseConfigSO.GoogleAppId"/> is empty.</exception>
        public FirebaseRemoteConfigProvider(FirebaseConfigSO config, IPlayerPrefs playerPrefs, Func<string, string, string, Task<string>> httpHandler)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.GoogleAppId))
                throw new ArgumentException("GoogleAppId cannot be empty", nameof(config));
            _playerPrefs = playerPrefs ?? throw new ArgumentNullException(nameof(playerPrefs));
            _httpHandler = httpHandler ?? throw new ArgumentNullException(nameof(httpHandler));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Cancellation aborts the HTTP request and throws <see cref="OperationCanceledException"/>;
        /// an answer that arrives after cancellation is never parsed.
        /// </remarks>
        public async Task<RemoteConfigFetchResult> FetchAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fidData = LoadFidData();
                if (string.IsNullOrEmpty(fidData?.Fid))
                {
                    var fid = await RegisterFidAsync(cancellationToken);
                    if (string.IsNullOrEmpty(fid))
                        return RemoteConfigFetchResult.Failure;
                    fidData = new FidData { Fid = fid };
                    SaveFidData(fidData);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var json = await FetchRemoteConfigAsync(fidData.Fid, cancellationToken);
                if (json == null)
                    return RemoteConfigFetchResult.Failure;
                return ParseResult(json);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigProvider)}: FetchAsync failed — {ex.Message}");
                return RemoteConfigFetchResult.Failure;
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
                Debug.LogError($"{nameof(FirebaseRemoteConfigProvider)}: failed to deserialize the cached FID data — {ex.Message}");
                return null;
            }
        }

        private void SaveFidData(FidData data)
        {
            var json = JsonConvert.SerializeObject(data);
            _playerPrefs.SetString(FidPrefsKey, json);
        }

        private async Task<string> RegisterFidAsync(CancellationToken cancellationToken)
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
                var response = await SendAsync("POST", url, bodyJson, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (response == null)
                {
                    Debug.LogError($"{nameof(FirebaseRemoteConfigProvider)}: failed to register the FID");
                    return null;
                }

                var j = JObject.Parse(response);
                var name = (string)j["name"];
                if (string.IsNullOrEmpty(name))
                {
                    Debug.LogError($"{nameof(FirebaseRemoteConfigProvider)}: the FID registration response has no 'name'");
                    return null;
                }

                var lastSlash = name.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash < name.Length - 1)
                    return name[(lastSlash + 1)..];

                return fid;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigProvider)}: FID registration failed — {ex.Message}");
                return null;
            }
        }

        private async Task<string> FetchRemoteConfigAsync(string fid, CancellationToken cancellationToken)
        {
            var url = $"https://firebaseremoteconfig.googleapis.com/v1/projects/{_config.ProjectNumber}/namespaces/firebase:fetch?key={_config.ApiKey}";

            var body = new
            {
                appId = _config.GoogleAppId,
                appInstanceId = fid
            };
            var bodyJson = JsonConvert.SerializeObject(body);

            var response = await SendAsync("POST", url, bodyJson, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return response;
        }

        private static RemoteConfigFetchResult ParseResult(string json)
        {
            try
            {
                var j = ParseKeepingStrings(json);

                // Without an ETag in the request the server always sends the full template, so
                // NO_CHANGE is unexpected; treat it as a failure so the cached values stay.
                if ((string)j["state"] == "NO_CHANGE")
                {
                    Debug.LogWarning($"{nameof(FirebaseRemoteConfigProvider)}: the server answered NO_CHANGE; the current values are kept");
                    return RemoteConfigFetchResult.Failure;
                }

                // No "entries" (state EMPTY_CONFIG or NO_TEMPLATE) means no parameters are published.
                Dictionary<string, string> parameters = null;
                var entries = (JObject)j["entries"];
                if (entries != null)
                {
                    parameters = new Dictionary<string, string>();
                    foreach (var prop in entries.Properties())
                    {
                        // Firebase sends every value as a string; keep it verbatim.
                        var value = prop.Value;
                        parameters[prop.Name] = value == null || value.Type == JTokenType.Null ? null
                            : value.Type == JTokenType.String ? (string)value
                            : value.ToString(Formatting.None);
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

                return RemoteConfigFetchResult.Success(parameters, experiments);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigProvider)}: failed to parse the response — {ex.Message}");
                return RemoteConfigFetchResult.Failure;
            }
        }

        // JObject.Parse turns ISO-8601 strings into DateTime, and ToString() then formats them in
        // the current culture ("2026-12-01T00:00:00Z" -> "12/01/2026 00:00:00"). Values must
        // reach the game exactly as published, so dates are not parsed.
        private static JObject ParseKeepingStrings(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        private Task<string> SendAsync(string method, string url, string body, CancellationToken cancellationToken) =>
            _httpHandler != null
                ? _httpHandler(method, url, body)
                : SendUnityWebRequestAsync(method, url, body, cancellationToken);

        private static async Task<string> SendUnityWebRequestAsync(string method, string url, string body, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"{nameof(FirebaseRemoteConfigProvider)}: HTTP {method} FAILED — {request.error} (code {request.responseCode}), body: {request.downloadHandler.text}");
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
