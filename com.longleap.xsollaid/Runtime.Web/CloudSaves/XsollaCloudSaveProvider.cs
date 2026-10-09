using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Cloud saves of one Xsolla ID account on our server (RewardService, the
    // /saves endpoints).
    //
    // The account is set at construction and never changes: the provider is
    // created only AFTER the player has signed in (the sign-in screen before the
    // menu), while a guest gets the null provider and local-only saves. There is
    // no need to switch owners on the fly — an account change always goes
    // through a scene reload.
    //
    // The server stores an entry as {v, ts}, while CloudSaveService hands out
    // and expects an envelope string with the same content. Conversion between
    // the two happens here and only through CloudSaveEnvelope, so the envelope
    // format lives in one place.
    //
    // The player is identified by xsollaId (sub) without a token — same as rewards.
    public sealed class XsollaCloudSaveProvider : ICloudSaveProvider, ICloudSaveBatchProvider
    {
        private const string SavesPath = "/saves";

        // A batch can weigh megabytes; the default 10 s are not meant for it.
        private const int BatchTimeoutSeconds = 60;

        // Server limit for a single value (CloudSaves:MaxValueBytes).
        private const int MaxValueBytes = 1024 * 1024;

        private readonly string _baseUrl;
        private readonly string _gameId;
        private readonly string _accountId;
        private readonly XsollaIdHttp _http;

        public XsollaCloudSaveProvider(string baseUrl, string gameId, string accountId, XsollaIdHttp http = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException($"{nameof(XsollaCloudSaveProvider)}: baseUrl is empty.", nameof(baseUrl));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException($"{nameof(XsollaCloudSaveProvider)}: gameId is empty.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException($"{nameof(XsollaCloudSaveProvider)}: accountId is empty.", nameof(accountId));

            _baseUrl = baseUrl.Trim().TrimEnd('/');
            _gameId = gameId.Trim();
            _accountId = accountId;
            _http = http ?? new XsollaIdHttp();
        }

        public string AccountId => _accountId;

        public async Task SetStringAsync(CloudSaveKey key, string value)
        {
            var (v, ts) = CloudSaveEnvelope.Parse(value);

            var body = new JObject
            {
                ["gameId"] = _gameId,
                ["xsollaId"] = _accountId,
                ["v"] = v,
                ["ts"] = ts
            };

            HttpReply reply = await SendAsync("PUT", KeyUrl(key), body.ToString(Formatting.None));

            EnsureSuccess(reply, $"SetStringAsync '{key.Value}'");
        }

        public async Task<string> GetStringAsync(CloudSaveKey key)
        {
            HttpReply reply = await _http.GetAsync(KeyUrl(key) + Query());

            if (reply.StatusCode == 404)
                return null;

            if (!reply.IsSuccess)
            {
                LogFailure(reply, $"GetStringAsync '{key.Value}'");
                return null;
            }

            try
            {
                var json = JObject.Parse(reply.Body);
                return CloudSaveEnvelope.Serialize((string)json["v"], (long?)json["ts"] ?? 0);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaCloudSaveProvider)}: failed to parse the value of '{key.Value}': {ex.Message}");
                return null;
            }
        }

        public async Task<bool> HasKeyAsync(CloudSaveKey key) =>
            await GetStringAsync(key) != null;

        public async Task DeleteKeyAsync(CloudSaveKey key)
        {
            HttpReply reply = await SendAsync("DELETE", KeyUrl(key) + Query(), null);

            EnsureSuccess(reply, $"DeleteKeyAsync '{key.Value}'");
        }

        // Never throws: CloudSaveService calls it from InitializeAsync, and a
        // failed load would bring down the whole bootstrap. An empty result on a
        // network failure is normal for a local-first service: the local mirror
        // stays as is, and dirty keys are pushed once the network is back.
        public async Task<Dictionary<string, string>> LoadAllAsync()
        {
            var result = new Dictionary<string, string>();
            var entries = await TryLoadEntriesAsync();

            if (entries == null)
                return result;

            foreach (var pair in entries)
                result[pair.Key] = CloudSaveEnvelope.Serialize(pair.Value.Value, pair.Value.TimestampUnixSeconds);

            return result;
        }

        public async Task DeleteAllAsync()
        {
            HttpReply reply = await SendAsync("DELETE", _baseUrl + SavesPath + Query(), null);

            EnsureSuccess(reply, "DeleteAllAsync");
        }

        public async Task SetManyAsync(
            IReadOnlyDictionary<string, CloudSaveEntry> entries,
            IReadOnlyCollection<string> deletes)
        {
            var payload = new JObject();

            if (entries != null)
            {
                foreach (var pair in entries)
                {
                    // The server would reject such a value (413) together with
                    // the whole batch, and the batch would fail on every push,
                    // keeping all other keys out of the cloud. Better to lose
                    // sync for one key (it is still intact locally) than for all.
                    int bytes = Encoding.UTF8.GetByteCount(pair.Value.Value ?? string.Empty);

                    if (bytes > MaxValueBytes)
                    {
                        Debug.LogError($"{nameof(XsollaCloudSaveProvider)}: '{pair.Key}' is {bytes} bytes, " +
                            $"over the server limit of {MaxValueBytes} — kept locally, not sent to the cloud.");
                        continue;
                    }

                    payload[pair.Key] = new JObject
                    {
                        ["v"] = pair.Value.Value,
                        ["ts"] = pair.Value.TimestampUnixSeconds
                    };
                }
            }

            // null instead of an entry means delete; the server handles it in the same batch.
            if (deletes != null)
            {
                foreach (string key in deletes)
                    payload[key] = JValue.CreateNull();
            }

            if (payload.Count == 0)
                return;

            var body = new JObject
            {
                ["gameId"] = _gameId,
                ["xsollaId"] = _accountId,
                ["entries"] = payload
            };

            HttpReply reply = await _http.RequestAsync(new HttpCall(
                "PUT", _baseUrl + SavesPath, body.ToString(Formatting.None), "application/json",
                timeoutSeconds: BatchTimeoutSeconds));

            EnsureSuccess(reply, $"SetManyAsync ({payload.Count} keys)");
        }

        // The account's whole save, already unpacked. null means it could not be
        // read (network, server), as opposed to an empty dictionary meaning "no
        // save". The sign-in screen needs the difference: saves can't be
        // compared without knowing what the account holds.
        public async Task<IReadOnlyDictionary<string, CloudSaveEntry>> TryLoadEntriesAsync()
        {
            HttpReply reply = await _http.GetAsync(_baseUrl + SavesPath + Query());

            if (!reply.IsSuccess)
            {
                LogFailure(reply, "LoadAllAsync");
                return null;
            }

            try
            {
                var entries = new Dictionary<string, CloudSaveEntry>();
                var json = JObject.Parse(reply.Body);

                foreach (var pair in json)
                {
                    if (!(pair.Value is JObject entry))
                        continue;

                    var value = (string)entry["v"];

                    if (value == null)
                        continue;

                    entries[pair.Key] = new CloudSaveEntry(value, (long?)entry["ts"] ?? 0);
                }

                return entries;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaCloudSaveProvider)}: failed to parse the save: {ex.Message}");
                return null;
            }
        }

        private Task<HttpReply> SendAsync(string method, string url, string jsonBody) =>
            _http.RequestAsync(new HttpCall(method, url, jsonBody, jsonBody != null ? "application/json" : null));

        private string KeyUrl(CloudSaveKey key) =>
            $"{_baseUrl}{SavesPath}/{Uri.EscapeDataString(key.Value)}";

        private string Query() =>
            $"?xsollaId={Uri.EscapeDataString(_accountId)}&gameId={Uri.EscapeDataString(_gameId)}";

        private static void EnsureSuccess(HttpReply reply, string what)
        {
            if (reply.IsSuccess)
                return;

            throw new InvalidOperationException($"{nameof(XsollaCloudSaveProvider)}: {what} failed — {Describe(reply)}");
        }

        private static void LogFailure(HttpReply reply, string what) =>
            Debug.LogWarning($"{nameof(XsollaCloudSaveProvider)}: {what} failed — {Describe(reply)}");

        private static string Describe(HttpReply reply) =>
            reply.IsTransportError
                ? reply.TransportError
                : $"HTTP {reply.StatusCode} {Truncate(reply.Body)}";

        private static string Truncate(string body) =>
            string.IsNullOrEmpty(body) || body.Length <= 200 ? body : body.Substring(0, 200) + "…";
    }
}
