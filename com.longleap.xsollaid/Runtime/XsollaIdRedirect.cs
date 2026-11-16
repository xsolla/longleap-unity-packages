using System;

namespace LongLeap.XsollaId
{
    // Where redirect_uri comes from.
    //
    // Why compute it instead of just reading the config: the callback page
    // ships INSIDE the build (XsollaIdCallbackPagePostprocessor puts it in the
    // root), so for every self-hosted build the correct address is right next
    // to the game. And there are usually several such hosts (local dev server,
    // static hosting, object storage). A single asset field for all of them
    // would mean editing the asset before every build — and a forgotten edit
    // in a release, where sign-in opens a window to a dead address.
    //
    // Portal builds are the exception, and their address cannot be computed:
    // the portal serves the game from its own origin, we cannot put our page
    // there, and the portal address is not registered in the Xsolla ID client
    // (and cannot be — the server compares redirect_uri character by
    // character). There the address comes from the config.
    //
    // A pure function without UnityEngine — like OriginGuard, and for the same
    // reason: a mistake here only shows up during a live sign-in, so it is
    // covered by tests. The failure reason is returned via an out parameter,
    // also like OriginGuard: logging belongs to the caller that has
    // UnityEngine, otherwise the class could not be tested without a scene.
    public static class XsollaIdRedirect
    {
        // Must match the file name the postprocessor puts into the build
        // (XsollaIdCallbackPagePostprocessor.FileName). If they diverge, the
        // sign-in window shows a 404.
        public const string CallbackFileName = "xid-callback.html";

        // Address of the callback page next to the game, or null if it cannot
        // be computed. null means "use the config value" — by itself it breaks
        // nothing.
        //
        // problem is why it failed, as a ready-made log phrase; null on
        // success. Without it, "taken from the asset" would look the same in
        // the log as "computed" — and that is exactly the fork that leads to
        // the server rejecting the sign-in window.
        //
        // Computed from the page's DIRECTORY, not just its origin: the build
        // may be served from a subfolder (https://host/game/index.html), and
        // the callback page sits there too, not at the domain root.
        public static string TryDerive(string pageUrl, out string problem)
        {
            problem = null;

            if (string.IsNullOrEmpty(pageUrl))
            {
                problem = "the page address is empty, which is normal in the editor and in a standalone player (there is no origin at all)";
                return null;
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                problem = $"the page address '{pageUrl}' is not an absolute URL";
                return null;
            }

            // Schemes like file:// are rejected: they have no origin the server
            // could accept.
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                problem = $"the page is served over '{uri.Scheme}', which has no origin the server would accept; open the build over http(s)";
                return null;
            }

            string origin = OriginGuard.TryGetOrigin(pageUrl);

            if (origin == null)
            {
                problem = $"the origin of '{pageUrl}' could not be determined";
                return null;
            }

            return origin + DirectoryOf(uri.AbsolutePath) + CallbackFileName;
        }

        // The page's directory, always with a trailing slash. Query and fragment
        // never get here: AbsolutePath does not contain them.
        private static string DirectoryOf(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return "/";

            int lastSlash = absolutePath.LastIndexOf('/');

            return lastSlash < 0 ? "/" : absolutePath.Substring(0, lastSlash + 1);
        }
    }
}
