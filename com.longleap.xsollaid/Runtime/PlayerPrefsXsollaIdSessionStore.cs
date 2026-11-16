using System;
using LongLeap.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.XsollaId
{
    // Xsolla ID session in PlayerPrefs (IndexedDB on WebGL).
    //
    // USES ITS OWN KEY AND NEVER TOUCHES OTHERS. The anonymous Firebase identity
    // lives under "longleap_auth" and must stay intact: it owns the cloud saves,
    // and it is what makes signing out return the player to THE SAME guest
    // identity instead of creating a new one with empty progress. There is a
    // dedicated test for this.
    public sealed class PlayerPrefsXsollaIdSessionStore : IXsollaIdSessionStore
    {
        private const string StoreKey = "longleap_xsollaid";

        private readonly IPlayerPrefs _prefs;

        public PlayerPrefsXsollaIdSessionStore(IPlayerPrefs prefs)
        {
            _prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
        }

        public XsollaIdSession Load()
        {
            string json = _prefs.GetString(StoreKey, null);

            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                JObject o = JObject.Parse(json);

                string subject = ReadString(o, "subject");
                string access = ReadString(o, "access");
                string refresh = ReadString(o, "refresh");

                // Without sub and refresh there is nothing to restore: quietly
                // treat it as no session.
                if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(refresh))
                {
                    Clear();
                    return null;
                }

                return new XsollaIdSession(
                    new XsollaUserId(subject),
                    ReadString(o, "displayName"),
                    string.IsNullOrEmpty(access) ? AccessToken.None : new AccessToken(access),
                    new RefreshToken(refresh),
                    ReadLong(o, "expiresAtUnixMs"),

                    // The url is re-validated on read instead of trusting the
                    // blob: it may have been written by a build with different
                    // rules.
                    AvatarUrl.TryParse(ReadString(o, "picture")),

                    // Missing key means the blob predates picture support. The
                    // profile is treated as not fetched: XsollaIdAuth fetches it
                    // once and rewrites the blob in the new format.
                    ReadBool(o, "profileFetched"),

                    // A blob written before these two keys existed yields nulls.
                    // No separate migration flag is needed: profileFetched above
                    // already decides whether to call userinfo, and the expanded
                    // card shows a dash for null.
                    ReadString(o, "userName"),
                    ReadString(o, "fullName"));
            }
            catch (Exception ex)
            {
                // A corrupted blob is no reason to crash at startup. Drop it and
                // carry on as a guest.
                Debug.LogWarning(
                    $"{nameof(PlayerPrefsXsollaIdSessionStore)}: failed to read the session, key cleared. {ex.Message}");
                Clear();
                return null;
            }
        }

        public void Save(XsollaIdSession session)
        {
            if (session == null)
            {
                Clear();
                return;
            }

            var o = new JObject
            {
                ["subject"] = session.Subject.Value,
                ["displayName"] = session.DisplayName,
                ["access"] = session.Access.Value,
                ["refresh"] = session.Refresh.Value,
                ["expiresAtUnixMs"] = session.ExpiresAtUnixMs,

                // These two keys are ALWAYS written, even without values. A
                // missing key marks a blob written before picture support, and
                // Load uses exactly that to decide the profile must be
                // re-fetched. Writing the key only for non-empty values would
                // break the marker and the migration would never happen.
                //
                // An empty url is written as "picture": null — the key is there,
                // and that is all that matters. displayName above behaves the
                // same way.
                ["picture"] = session.Picture.Value,
                ["profileFetched"] = session.ProfileFetched,

                // Also always, nulls included — for the same reason as picture
                // above.
                ["userName"] = session.UserName,
                ["fullName"] = session.FullName
            };

            _prefs.SetString(StoreKey, o.ToString(Newtonsoft.Json.Formatting.None));
            _prefs.Save();
        }

        public void Clear()
        {
            _prefs.DeleteKey(StoreKey);
            _prefs.Save();
        }

        // Blob fields are read strictly by type, not via (string)o[key].
        //
        // Casting a field that arrived as an object or array throws
        // InvalidCastException — caught by the catch in Load, which CLEARS THE
        // KEY. So a single garbage field would cost the player the whole
        // session. The blob may have been written by a build with different
        // rules, and on WebGL it is IndexedDB, which the player can edit by
        // hand.
        private static string ReadString(JObject o, string key) =>
            o[key] is JValue value && value.Type == JTokenType.String ? (string)value : null;

        private static bool ReadBool(JObject o, string key) =>
            o[key] is JValue value && value.Type == JTokenType.Boolean && (bool)value;

        private static long ReadLong(JObject o, string key) =>
            o[key] is JValue value && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                ? (long)value
                : 0L;
    }
}
