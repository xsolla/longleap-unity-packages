using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.Core.Auth;
using LongLeap.Core.CloudSaves;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.Firebase
{
    /// <summary>
    /// Cloud saves stored in Firebase Realtime Database over REST.
    /// </summary>
    /// <remarks>
    /// Guest saves live at <c>cloud_saves/{uid}/{key}</c>, where <c>uid</c> is the anonymous
    /// Firebase uid. After <see cref="UseAccount"/> all operations go to
    /// <c>xsolla_saves/{accountId}/{key}</c> instead. Every request is signed with the
    /// anonymous Firebase ID token, in both scopes.
    /// </remarks>
    public sealed class FirebaseCloudSaveProvider : ICloudSaveProvider, IAccountCloudSaveScope
    {
        private const string RootPath = "cloud_saves";

        // Account saves live in a SEPARATE branch, not under cloud_saves: the guest
        // save must survive both sign-in and sign-out, so its path must not change.
        private const string AccountRootPath = "xsolla_saves";

        // The selected scope, persisted so it survives a scene reload and a game
        // restart.
        //
        // It is read HERE instead of waiting for the Xsolla session restore: the
        // restore is started without being awaited, and by the time
        // CloudSaveService.InitializeAsync runs the session may not exist yet. Had
        // we waited for it, the first load would come from the guest branch and the
        // player would see someone else's progress.
        private const string ScopePrefsKey = "longleap_cloudsave_scope";

        private readonly FirebaseHttpClient _http;
        private readonly IAuthProvider _auth;
        private readonly IPlayerPrefs _prefs;
        private string _uid;
        private string _accountId;

        /// <param name="http">RTDB client used for all reads and writes.</param>
        /// <param name="auth">Resolves the anonymous uid for the guest branch.</param>
        /// <param name="playerPrefs">
        /// Persists the selected scope across launches. Optional: when <c>null</c> the
        /// provider always starts in guest scope.
        /// </param>
        public FirebaseCloudSaveProvider(FirebaseHttpClient http, IAuthProvider auth, IPlayerPrefs playerPrefs = null)
        {
            _http = http ?? throw new System.ArgumentNullException(nameof(http));
            _auth = auth ?? throw new System.ArgumentNullException(nameof(auth));
            _prefs = playerPrefs;

            string saved = _prefs?.GetString(ScopePrefsKey, null);

            if (!string.IsNullOrEmpty(saved) && IsValidAccountId(saved))
                _accountId = saved;
        }

        public bool IsAccountScoped => !string.IsNullOrEmpty(_accountId);

        public string AccountId => _accountId ?? "";

        public void UseAccount(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
                throw new System.ArgumentException(
                    $"{nameof(FirebaseCloudSaveProvider)}: accountId cannot be empty — use UseGuest.", nameof(accountId));

            // The account id becomes an RTDB path segment, so it has the same
            // requirements as a save key. Letting garbage through would write to a
            // branch that could never be read back.
            if (!IsValidAccountId(accountId))
                throw new System.ArgumentException(
                    $"{nameof(FirebaseCloudSaveProvider)}: accountId contains forbidden characters (. $ # [ ] /).", nameof(accountId));

            _accountId = accountId;
            _prefs?.SetString(ScopePrefsKey, accountId);
            _prefs?.Save();

            Debug.Log($"{nameof(FirebaseCloudSaveProvider)}: switched to account scope");
        }

        public void UseGuest()
        {
            _accountId = null;
            _prefs?.DeleteKey(ScopePrefsKey);
            _prefs?.Save();

            Debug.Log($"{nameof(FirebaseCloudSaveProvider)}: switched to guest scope");
        }

        public async Task<IReadOnlyDictionary<string, CloudSaveEntry>> PeekAccountAsync(string accountId)
        {
            var empty = new Dictionary<string, CloudSaveEntry>();

            if (string.IsNullOrEmpty(accountId) || !IsValidAccountId(accountId))
                return empty;

            string response = await _http.GetAsync($"{AccountRootPath}/{accountId}");

            if (response == null || response == "null")
                return empty;

            try
            {
                var entries = new Dictionary<string, CloudSaveEntry>();
                var json = JObject.Parse(response);

                foreach (var kvp in json)
                {
                    // Strings only, same as LoadAllAsync: anything else was not
                    // written by this provider.
                    if (kvp.Value.Type != JTokenType.String)
                        continue;

                    string raw = kvp.Value.Value<string>();

                    if (raw == null)
                        continue;

                    var (value, ts) = CloudSaveEnvelope.Parse(raw);

                    if (value != null)
                        entries[kvp.Key] = new CloudSaveEntry(value, ts);
                }

                return entries;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{nameof(FirebaseCloudSaveProvider)}: PeekAccountAsync parse failed — {ex.Message}");
                return empty;
            }
        }

        private static bool IsValidAccountId(string accountId) =>
            accountId != null && accountId.IndexOfAny(ForbiddenPathChars) < 0;

        private static readonly char[] ForbiddenPathChars = { '.', '$', '#', '[', ']', '/' };

        // The single place that decides whose branch a request goes to. Every
        // method below goes through it; otherwise one forgotten line would send
        // part of the operations to someone else's save.
        //
        // The account branch does not need the uid, but the anonymous token is
        // still required: every RTDB request is signed with it.
        private async Task<string> ResolveBasePathAsync()
        {
            if (IsAccountScoped)
                return $"{AccountRootPath}/{_accountId}";

            string uid = await ResolveUidAsync();

            return uid == null ? null : $"{RootPath}/{uid}";
        }

        private async Task<string> ResolveUidAsync()
        {
            if (!string.IsNullOrEmpty(_uid))
                return _uid;

            var result = await _auth.LoginAsync();
            if (result is AuthResult.Success success)
            {
                _uid = success.Uid.Value;
                return _uid;
            }

            return null;
        }

        public async Task SetStringAsync(CloudSaveKey key, string value)
        {
            string basePath = await ResolveBasePathAsync();
            if (basePath == null)
                throw new System.InvalidOperationException(
                    $"{nameof(FirebaseCloudSaveProvider)}: failed to resolve the uid for SetStringAsync, key '{key.Value}'");
            await _http.PutAsync($"{basePath}/{key.Value}", JsonConvert.SerializeObject(value));
        }

        public async Task<string> GetStringAsync(CloudSaveKey key)
        {
            string basePath = await ResolveBasePathAsync();
            if (basePath == null) return null;
            string response = await _http.GetAsync($"{basePath}/{key.Value}");
            if (response == null || response == "null") return null;
            try { return JsonConvert.DeserializeObject<string>(response); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{nameof(FirebaseCloudSaveProvider)}: failed to deserialize the value for key {key.Value}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> HasKeyAsync(CloudSaveKey key)
        {
            string basePath = await ResolveBasePathAsync();
            if (basePath == null) return false;
            string response = await _http.GetAsync($"{basePath}/{key.Value}");
            return response != null && response != "null";
        }

        public async Task DeleteKeyAsync(CloudSaveKey key)
        {
            string basePath = await ResolveBasePathAsync();
            if (basePath == null)
                throw new System.InvalidOperationException(
                    $"{nameof(FirebaseCloudSaveProvider)}: failed to resolve the uid for DeleteKeyAsync, key '{key.Value}'");
            await _http.DeleteAsync($"{basePath}/{key.Value}");
        }

        public async Task<Dictionary<string, string>> LoadAllAsync()
        {
            string basePath = await ResolveBasePathAsync();
            if (basePath == null) return new Dictionary<string, string>();

            string response = await _http.GetAsync(basePath);
            if (response == null || response == "null") return new Dictionary<string, string>();

            try
            {
                var dict = new Dictionary<string, string>();
                var json = JObject.Parse(response);
                foreach (var kvp in json)
                {
                    if (kvp.Value.Type == JTokenType.String)
                        dict[kvp.Key] = kvp.Value.Value<string>();
                }
                return dict;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{nameof(FirebaseCloudSaveProvider)}: failed to parse the LoadAllAsync response: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        public async Task DeleteAllAsync()
        {
            string basePath = await ResolveBasePathAsync();
            if (basePath == null)
                throw new System.InvalidOperationException(
                    $"{nameof(FirebaseCloudSaveProvider)}: failed to resolve the uid for DeleteAllAsync");
            await _http.DeleteAsync(basePath);
        }
    }
}
