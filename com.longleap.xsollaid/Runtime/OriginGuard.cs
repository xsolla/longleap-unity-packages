using System;
using System.Collections.Generic;

namespace LongLeap.XsollaId
{
    // Checks that the game is served from an origin where sign-in can work at
    // all.
    //
    // Why: the server compares redirect_uri character by character and rejects
    // a mismatch — but that only shows up AFTER the window opens, with a vague
    // message. It is cheaper to check upfront and say it plainly.
    //
    // Why a list rather than a comparison with the redirect uri: sign-in also
    // works in portal builds, where the portal (e.g. GamePix) serves the game
    // from its own origin while the callback page may live on our domain. So
    // the game origin and the redirect uri origin NEED NOT MATCH, and that
    // cannot be required. Membership in the allowed list is checked instead.
    //
    // Catches two real cases:
    //   1) the local dev server prints 127.0.0.1, but the developer types
    //      localhost out of habit — these are DIFFERENT origins, and sign-in
    //      fails;
    //   2) the production config still lists a wrong domain — caught on the
    //      first click, not from a player report.
    //
    // A pure function without UnityEngine, so it can be unit-tested.
    public static class OriginGuard
    {
        // Whether the page origin is allowed.
        //
        // pageUrl is the full page address (Application.absoluteURL in game).
        // An empty pageUrl (editor, standalone) passes: there is no origin.
        //
        // An empty allowedOrigins also passes: no list means nothing to
        // restrict, let the server decide. Silently blocking sign-in because of
        // an unfilled config is worse than letting it through.
        public static bool IsAllowed(
            string pageUrl, IReadOnlyList<string> allowedOrigins, out string problem)
        {
            problem = null;

            if (string.IsNullOrEmpty(pageUrl))
                return true;

            if (allowedOrigins == null || allowedOrigins.Count == 0)
                return true;

            string pageOrigin = TryGetOrigin(pageUrl);

            if (pageOrigin == null)
            {
                // Could not parse it — do not get in the way.
                return true;
            }

            var known = new List<string>(allowedOrigins.Count);

            foreach (string allowed in allowedOrigins)
            {
                string origin = TryGetOrigin(allowed);

                if (origin == null)
                    continue;

                known.Add(origin);

                if (string.Equals(pageOrigin, origin, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Nothing matched — but if none of the entries could be parsed,
            // treat it as if there were no list.
            if (known.Count == 0)
                return true;

            problem =
                $"the game is served from {pageOrigin}, but sign-in is allowed only from {string.Join(", ", known)}. " +
                $"The server compares the address character by character, so the sign-in window would open for nothing.";

            return false;
        }

        // scheme://host:port — what the browser considers the origin. The
        // default port is omitted: both sides are normalized the same way, so
        // the short form matches itself.
        public static string TryGetOrigin(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return null;

            return uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.Host}"
                : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }
    }
}
