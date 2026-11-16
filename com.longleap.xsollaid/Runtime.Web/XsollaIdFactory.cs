using LongLeap.Core;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Assembles all sign-in parts in one place.
    //
    // Exists so that game code needs exactly one #if XSOLLA_ID: without the
    // factory the game's bootstrap would have to mention four types from the
    // define-gated assembly, each needing its own guard.
    public static class XsollaIdFactory
    {
        // Returns a working sign-in, or a stub if the config is incomplete.
        //
        // A stub instead of an error on purpose: an empty clientId is a normal
        // state for a branch that has no client registration yet, and the game
        // must not fail because of it. The UI simply hides the account block.
        // deriveRedirectFromPage — whether the page hosting the game is also
        // where the callback page lives. That way every self-hosted build (local
        // dev server, static hosting, object storage) gets its redirect_uri
        // automatically, without editing the asset before each build. Portal
        // builds pass false: the portal serves the game, we cannot put our page
        // there, and the address comes from the asset — see XsollaIdRedirect.
        public static IXsollaIdAuth Create(
            XsollaIdConfigSO configAsset, IPlayerPrefs prefs, bool deriveRedirectFromPage = true)
        {
            if (configAsset == null || prefs == null)
                return new UnavailableXsollaIdAuth();

            XsollaIdConfig config = configAsset.ToConfig();

            // Log the redirect choice in detail, always, in all four outcomes.
            //
            // A redirect_uri rejection comes FROM THE SERVER, after the window
            // has opened, and is vaguely worded — it can only be diagnosed by
            // knowing exactly which address was sent and where it came from.
            string pageUrl = Application.absoluteURL;
            string assetRedirect = config.Active?.RedirectUri;

            if (deriveRedirectFromPage)
            {
                string derived = XsollaIdRedirect.TryDerive(pageUrl, out string problem);

                config = config.WithRedirectUri(derived);

                if (derived != null)
                {
                    Debug.Log($"{nameof(XsollaIdFactory)}: redirect_uri derived from the game's own address — " +
                        $"page '{pageUrl}' gives '{derived}' (the asset says '{assetRedirect}'). " +
                        "It must be registered in the Xsolla ID client character for character.");
                }
                else if (Application.isEditor)
                {
                    // There is no sign-in in the editor at all (the browser's
                    // IsSupported is false), so this is a plain log, not a
                    // warning: otherwise it would fire on every Play Mode run.
                    Debug.Log($"{nameof(XsollaIdFactory)}: redirect_uri not derived — {problem}. " +
                        $"Falling back to the asset value '{assetRedirect}'. Expected in the editor.");
                }
                else
                {
                    Debug.LogWarning($"{nameof(XsollaIdFactory)}: redirect_uri NOT derived — {problem}. " +
                        $"Falling back to the asset value '{assetRedirect}', which is the address sign-in " +
                        "will send. Check that it is the one registered for this host.");
                }
            }
            else
            {
                Debug.Log($"{nameof(XsollaIdFactory)}: portal build — the platform serves the game " +
                    $"(page '{pageUrl}'), so the callback page cannot live next to it. redirect_uri comes " +
                    $"from the config asset — '{assetRedirect}'. That address must serve xid-callback.html " +
                    "and list this page's origin in its own ALLOWED_GAME_ORIGINS.");
            }

            if (!config.IsValid)
            {
                // Must not stay silent: a stub goes out, the UI hides the
                // account block, and "sign-in just isn't there" would have no
                // explanation.
                Debug.LogWarning($"{nameof(XsollaIdFactory)}: sign-in is unavailable — the active " +
                    $"{config.Environment} registration is incomplete (clientId " +
                    $"'{config.Active?.ClientId}', redirect_uri '{config.Active?.RedirectUri}', issuer " +
                    $"'{config.Active?.IssuerBaseUrl}'). Fill it in Resources/Backend/XsollaIdConfig.");

                return new UnavailableXsollaIdAuth();
            }

            Debug.Log($"{nameof(XsollaIdFactory)}: sign-in ready — environment {config.Environment}, " +
                $"issuer '{config.Active.IssuerBaseUrl}', effective redirect_uri '{config.Active.RedirectUri}'.");

            var http = new XsollaIdHttp();
            var discovery = new OidcDiscovery(config.Active.DiscoveryUrl, http);

            return new XsollaIdAuth(
                config,
                discovery,
                new XsollaIdWebBrowser(),
                new XsollaIdTokenClient(config, discovery, http),
                new PlayerPrefsXsollaIdSessionStore(prefs));
        }

        // The avatar loader is separate from sign-in: it depends on neither the
        // config nor discovery, and the UI needs it even when sign-in is
        // unavailable — then game code uses UnavailableXsollaIdAvatarLoader and
        // shows the placeholder, again without a single #if.
        public static IXsollaIdAvatarLoader CreateAvatarLoader() => new XsollaIdAvatarLoader();
    }
}
