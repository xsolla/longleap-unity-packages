using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Endpoint addresses of the authorization server.
    //
    // The paths are deliberately NOT hardcoded. Xsolla ID currently serves its
    // Hydra endpoints under /api/oauth2/*, while the discovery document
    // declares them at the root (/oauth2/*); the planned migration moves
    // everything to the root, keeps both paths for a while and then turns
    // /api/oauth2/* off. Hardcoding today's path guarantees breakage at that
    // point.
    public sealed class OidcConfiguration
    {
        public string Issuer { get; }
        public string AuthorizationEndpoint { get; }
        public string TokenEndpoint { get; }
        public string UserInfoEndpoint { get; }
        public string RevocationEndpoint { get; }

        public OidcConfiguration(
            string issuer,
            string authorizationEndpoint,
            string tokenEndpoint,
            string userInfoEndpoint,
            string revocationEndpoint)
        {
            Issuer = issuer;
            AuthorizationEndpoint = authorizationEndpoint;
            TokenEndpoint = tokenEndpoint;
            UserInfoEndpoint = userInfoEndpoint;
            RevocationEndpoint = revocationEndpoint;
        }

        // The minimum required for sign-in. userinfo and revoke are optional:
        // without the first the UI has no name to show, without the second
        // sign-out stays local only.
        public bool IsUsable =>
            !string.IsNullOrEmpty(AuthorizationEndpoint) && !string.IsNullOrEmpty(TokenEndpoint);
    }

    // Loads /.well-known/openid-configuration.
    //
    // The result MUST be cached and loaded ahead of time, at startup: fetching
    // it between the player's click and window.open gets the window blocked by
    // the browser — the user gesture has expired by then.
    //
    // The Xsolla ID document has twice disagreed with reality in production
    // (end_session_endpoint served the login page, revocation_endpoint returned
    // 405), so its contents must be validated, not taken on trust.
    public interface IOidcDiscovery
    {
        Task<OidcConfiguration> GetAsync();
    }
}
