using System;
using LongLeap.Core;

namespace LongLeap.Analytics
{
    // A random, persistent ID for this installation: GA4's client_id. GA4
    // counts users by it, so it must survive restarts and must not be derived
    // from personal data.
    //
    //     var clientId = AnalyticsClientId.GetOrCreate(new UnityPlayerPrefsAdapter());
    //
    // When the player withdraws consent, call Reset so a later opt-in starts
    // as a new, unlinked user.
    public static class AnalyticsClientId
    {
        public const string DefaultKey = "longleap.analytics.client_id";

        public static string GetOrCreate(IPlayerPrefs prefs, string key = DefaultKey)
        {
            if (prefs == null)
                throw new ArgumentNullException(nameof(prefs));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key is empty.", nameof(key));

            var id = prefs.GetString(key, string.Empty);
            if (!string.IsNullOrEmpty(id))
                return id;

            id = Guid.NewGuid().ToString("N");
            prefs.SetString(key, id);
            prefs.Save();
            return id;
        }

        public static void Reset(IPlayerPrefs prefs, string key = DefaultKey)
        {
            if (prefs == null)
                throw new ArgumentNullException(nameof(prefs));

            prefs.DeleteKey(key);
            prefs.Save();
        }
    }
}
