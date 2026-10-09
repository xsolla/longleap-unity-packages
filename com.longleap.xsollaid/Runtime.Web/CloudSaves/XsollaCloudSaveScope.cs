using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.Core.CloudSaves;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Whose save sits in the local mirror: the guest's or an account's (and which).
    //
    // The provider doesn't remember this by itself — it is created for a single
    // account, and a guest gets the null provider. But on the next launch the
    // sign-in screen needs to know whose data is in PlayerPrefs right now: that
    // decides whether to compare it with the account save, clear it, or keep it.
    //
    // The PlayerPrefs key is the same as FirebaseCloudSaveProvider's on purpose:
    // a player who already signed in on the previous build keeps that account's
    // save in the mirror, and there is no reason to ask them again.
    public sealed class XsollaCloudSaveScope : IAccountCloudSaveScope
    {
        private const string ScopePrefsKey = "longleap_cloudsave_scope";

        private readonly string _baseUrl;
        private readonly string _gameId;
        private readonly IPlayerPrefs _prefs;
        private readonly XsollaIdHttp _http;
        private string _accountId;

        public XsollaCloudSaveScope(string baseUrl, string gameId, IPlayerPrefs prefs, XsollaIdHttp http = null)
        {
            _baseUrl = baseUrl;
            _gameId = gameId;
            _prefs = prefs ?? throw new System.ArgumentNullException(nameof(prefs));
            _http = http;

            string saved = _prefs.GetString(ScopePrefsKey, null);

            if (!string.IsNullOrEmpty(saved))
                _accountId = saved;
        }

        public bool IsAccountScoped => !string.IsNullOrEmpty(_accountId);

        public string AccountId => _accountId ?? "";

        public void UseAccount(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
                throw new System.ArgumentException(
                    $"{nameof(XsollaCloudSaveScope)}: accountId cannot be empty — use UseGuest.", nameof(accountId));

            _accountId = accountId;
            _prefs.SetString(ScopePrefsKey, accountId);
            _prefs.Save();

            Debug.Log($"{nameof(XsollaCloudSaveScope)}: the local save now belongs to the account");
        }

        public void UseGuest()
        {
            _accountId = null;
            _prefs.DeleteKey(ScopePrefsKey);
            _prefs.Save();

            Debug.Log($"{nameof(XsollaCloudSaveScope)}: the local save now belongs to the guest");
        }

        // Per the interface contract this never throws and doesn't distinguish a
        // failure from an empty save. Callers who care (the sign-in screen) use
        // TryPeekAccountAsync.
        public async Task<IReadOnlyDictionary<string, CloudSaveEntry>> PeekAccountAsync(string accountId) =>
            await TryPeekAccountAsync(accountId) ?? new Dictionary<string, CloudSaveEntry>();

        // The account's save on the server, WITHOUT touching the mirror. null
        // means it could not be read.
        public async Task<IReadOnlyDictionary<string, CloudSaveEntry>> TryPeekAccountAsync(string accountId)
        {
            if (string.IsNullOrEmpty(accountId) || string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(_gameId))
                return null;

            var provider = new XsollaCloudSaveProvider(_baseUrl, _gameId, accountId, _http);

            return await provider.TryLoadEntriesAsync();
        }
    }
}
