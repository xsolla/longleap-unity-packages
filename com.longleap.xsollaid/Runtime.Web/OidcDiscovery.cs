using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LongLeap.XsollaId.Web
{
    // Loads /.well-known/openid-configuration and caches it.
    //
    // Endpoint paths are deliberately NOT hardcoded: Xsolla ID is moving them
    // from /api/oauth2/* to the root (both paths live side by side for a while,
    // then the old one is turned off). Hardcoding today's path would break on
    // the switch-off.
    //
    // The document cannot be trusted blindly either: it has disagreed with
    // reality in production. At the time of writing, production serves
    // authorize/token/userinfo under /api/oauth2/*, while revocation_endpoint
    // and end_session_endpoint point at the root — exactly the two that broke.
    // Hence two decisions: token revocation is best-effort with a second-path
    // retry, and end_session is not used at all.
    public sealed class OidcDiscovery : IOidcDiscovery
    {
        private readonly string _discoveryUrl;
        private readonly XsollaIdHttp _http;

        // Cache the task itself: concurrent calls share one load, later calls
        // get the finished result. Warming up at startup ensures the address is
        // known by the time of the click: fetching it between the click and
        // window.open is not an option — the browser would treat the window as
        // unsolicited.
        private Task<OidcConfiguration> _inFlight;

        public OidcDiscovery(string discoveryUrl, XsollaIdHttp http = null)
        {
            if (string.IsNullOrEmpty(discoveryUrl))
                throw new ArgumentException("OidcDiscovery: the discovery address is not set.", nameof(discoveryUrl));

            _discoveryUrl = discoveryUrl;
            _http = http ?? new XsollaIdHttp();
        }

        public Task<OidcConfiguration> GetAsync()
        {
            // A failed attempt is not cached: the network may have just blinked.
            if (_inFlight != null && !IsFailedAttempt(_inFlight))
                return _inFlight;

            _inFlight = LoadAsync();

            return _inFlight;
        }

        // IsFaulted alone is not enough. LoadAsync and Parse swallow their own
        // failures and return null normally, so a failure looks like a
        // successfully completed task with a null result. Without the null
        // check, a single request cut off by CORS or a network blip during
        // warm-up would kill sign-in for the rest of the session: every click
        // would take the same null from the cache and fail with NotConfigured
        // without ever hitting the network.
        //
        // Result is read only on a successfully completed task — it is ready
        // there and does not throw.
        private static bool IsFailedAttempt(Task<OidcConfiguration> attempt) =>
            attempt.IsFaulted ||
            attempt.IsCanceled ||
            (attempt.Status == TaskStatus.RanToCompletion && attempt.Result == null);

        private async Task<OidcConfiguration> LoadAsync()
        {
            HttpReply reply = await _http.GetAsync(_discoveryUrl);

            if (!reply.IsSuccess)
            {
                XsollaIdHttp.LogWarning(reply.IsTransportError
                    ? $"discovery unavailable — {reply.TransportError}"
                    : $"discovery responded {reply.StatusCode}");

                return null;
            }

            return Parse(reply.Body, _discoveryUrl);
        }

        // Parsing is public and network-free so it can be tested against real
        // responses, including deliberately malformed ones.
        public static OidcConfiguration Parse(string json, string expectedDiscoveryUrl = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                JObject o = JObject.Parse(json);

                var config = new OidcConfiguration(
                    (string)o["issuer"],
                    (string)o["authorization_endpoint"],
                    (string)o["token_endpoint"],
                    (string)o["userinfo_endpoint"],
                    (string)o["revocation_endpoint"]);

                if (!config.IsUsable)
                {
                    XsollaIdHttp.LogWarning("discovery did not return authorization_endpoint or token_endpoint.");
                    return null;
                }

                // Endpoints must live on the same host as the document itself.
                // A spoofed (or simply misconfigured) discovery pointing at a
                // foreign host would send the authorization code elsewhere — a
                // cheap check against an expensive mistake.
                if (!SameHost(expectedDiscoveryUrl, config.AuthorizationEndpoint) ||
                    !SameHost(expectedDiscoveryUrl, config.TokenEndpoint))
                {
                    XsollaIdHttp.LogWarning(
                        "discovery points its endpoints at a different host — document rejected.");
                    return null;
                }

                return config;
            }
            catch (Exception ex)
            {
                XsollaIdHttp.LogWarning($"discovery could not be parsed: {ex.Message}");
                return null;
            }
        }

        // Empty expected — nothing to compare against, let it through.
        private static bool SameHost(string expected, string candidate)
        {
            if (string.IsNullOrEmpty(expected))
                return true;

            if (!Uri.TryCreate(expected, UriKind.Absolute, out Uri a) ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out Uri b))
            {
                return false;
            }

            return string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
        }
    }
}
