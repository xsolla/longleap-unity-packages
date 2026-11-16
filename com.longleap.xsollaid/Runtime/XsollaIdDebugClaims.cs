#if XSOLLA_ID_DEBUG
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LongLeap.XsollaId
{
    // Snapshot of the claims from the last parsed userinfo — for the debug
    // panel only.
    //
    // The whole file is behind XSOLLA_ID_DEBUG and never reaches a release
    // build: it holds a live player's email and name, and there is no reason to
    // keep them in release memory. XsollaIdDebugPanel and FakeXsollaIdAuth are
    // gated the same way.
    //
    // Why a snapshot rather than a field in XsollaIdSession: the session is
    // serialized to PlayerPrefs (PlayerPrefsXsollaIdSessionStore), so the
    // claims would end up on disk and change the blob format. Debugging does
    // not need that, and the player even less so.
    //
    // The static state survives scene reloads, which is exactly what is wanted
    // here: userinfo is called once per identity, while the panel is recreated.
    // In exchange, the panel MUST show the snapshot only for a signed-in
    // player — otherwise someone else's claims would stay on screen after
    // sign-out.
    public static class XsollaIdDebugClaims
    {
        // The same four claims XsollaIdTokenClient.PickDisplayName chooses the
        // name from, in the same order: the first non-empty one becomes
        // DisplayName. The order matters — it shows on the panel which claim
        // won and which were left unused.
        public static readonly string[] Watched =
        {
            "username", "preferred_username", "name", "email"
        };

        private static readonly Dictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string[] _allKeys = Array.Empty<string>();

        // false means userinfo has not happened in this run. That is itself
        // informative: a restored session with ProfileFetched = true makes no
        // request at all, and the name comes from PlayerPrefs, not the server.
        public static bool HasSnapshot { get; private set; }

        // Only the NAMES of all response claims, without values: they show
        // whether the response contains nickname or given_name at all, without
        // putting anything on screen that was not asked for.
        public static IReadOnlyList<string> AllKeys => _allKeys;

        public static void Capture(JObject claims)
        {
            if (claims == null)
                return;

            Values.Clear();

            var keys = new List<string>();

            foreach (KeyValuePair<string, JToken> pair in claims)
            {
                keys.Add(pair.Key);

                // A non-string claim is shown as raw JSON: ReadStringClaim reads
                // values strictly by type, so PickDisplayName skips such a
                // claim — and the panel should show that a value exists but no
                // name will be taken from it.
                Values[pair.Key] = pair.Value is JValue value && value.Type == JTokenType.String
                    ? (string)value
                    : pair.Value?.ToString(Formatting.None);
            }

            keys.Sort(StringComparer.Ordinal);

            _allKeys = keys.ToArray();
            HasSnapshot = true;
        }

        // Three deliberately distinct answers: "?" — userinfo was not
        // requested, "-" — requested, but the claim is missing (or empty),
        // anything else — the value. An empty string on the panel would read
        // as "unknown".
        public static string Value(string key)
        {
            if (!HasSnapshot)
                return "?";

            return Values.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)
                ? value
                : "-";
        }
    }
}
#endif
