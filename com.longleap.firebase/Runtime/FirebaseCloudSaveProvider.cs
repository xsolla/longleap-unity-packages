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
    public sealed class FirebaseCloudSaveProvider : ICloudSaveProvider, IAccountCloudSaveScope
    {
        private const string RootPath = "cloud_saves";

        // Сейвы аккаунта лежат ОТДЕЛЬНОЙ ветвью, а не под cloud_saves: гостевой
        // сейв обязан пережить и вход, и выход, а значит его путь трогать нельзя.
        private const string AccountRootPath = "xsolla_saves";

        // Выбранная область, чтобы она пережила перезагрузку сцены и запуск игры
        // заново.
        //
        // Читаем её ЗДЕСЬ, а не ждём восстановления сессии Xsolla: восстановление
        // запускают без ожидания, и к моменту CloudSaveService.InitializeAsync
        // сессии может ещё не быть. Дождавшись её, первую загрузку мы бы сделали
        // из гостевой ветви, а игрок увидел бы чужой прогресс.
        private const string ScopePrefsKey = "longleap_cloudsave_scope";

        private readonly FirebaseHttpClient _http;
        private readonly IAuthProvider _auth;
        private readonly IPlayerPrefs _prefs;
        private string _uid;
        private string _accountId;

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

            // Идентификатор аккаунта становится сегментом пути RTDB, поэтому к
            // нему те же требования, что к ключу сейва. Пропустив мусор, мы
            // писали бы в ветвь, которую потом не прочитать.
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
                    // Только строки, как в LoadAllAsync: всё остальное записано
                    // не нами.
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

        // Единственное место, где решается, в чью ветвь идёт запрос. Все методы
        // ниже зовут только его — иначе одна забытая строка увела бы часть
        // операций в чужой сейв.
        //
        // Аккаунтная ветвь uid не требует, но анонимный токен всё равно нужен:
        // им подписан любой запрос к RTDB.
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
                    $"{nameof(FirebaseCloudSaveProvider)}: не удалось получить uid для SetStringAsync ключа '{key.Value}'");
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
                    $"{nameof(FirebaseCloudSaveProvider)}: не удалось получить uid для DeleteKeyAsync ключа '{key.Value}'");
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
                    $"{nameof(FirebaseCloudSaveProvider)}: не удалось получить uid для DeleteAllAsync");
            await _http.DeleteAsync(basePath);
        }
    }
}
