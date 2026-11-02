using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LongLeap.XsollaId.Web
{
    // Code exchange, token refresh, userinfo and revocation.
    //
    // Never throws: failures come back as an XsollaIdErrorCode, because these
    // tasks are polled by a bootstrap coroutine upstream, and an exception
    // there means a hung loading screen.
    public sealed class XsollaIdTokenClient : IXsollaIdTokenClient
    {
        private readonly XsollaIdConfig _config;
        private readonly IOidcDiscovery _discovery;
        private readonly XsollaIdHttp _http;
        private readonly Func<long> _nowUnixMs;

        public XsollaIdTokenClient(
            XsollaIdConfig config,
            IOidcDiscovery discovery,
            XsollaIdHttp http = null,
            Func<long> nowUnixMs = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _http = http ?? new XsollaIdHttp();
            _nowUnixMs = nowUnixMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public async Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri)
        {
            OidcConfiguration oidc = await _discovery.GetAsync();

            if (oidc == null)
                return TokenResponse.Failed(XsollaIdErrorCode.NotConfigured, "discovery unavailable.");

            // Public client: there is no secret, code_verifier proves
            // authenticity. client_id goes in the body.
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = _config.Active.ClientId
            };

            HttpReply reply = await _http.PostFormAsync(oidc.TokenEndpoint, form);

            return ReadTokens(reply, "code exchange");
        }

        public async Task<TokenResponse> RefreshAsync(string refreshToken)
        {
            OidcConfiguration oidc = await _discovery.GetAsync();

            if (oidc == null)
                return TokenResponse.Failed(XsollaIdErrorCode.NotConfigured, "discovery unavailable.");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _config.Active.ClientId
            };

            HttpReply reply = await _http.PostFormAsync(oidc.TokenEndpoint, form);

            return ReadTokens(reply, "token refresh");
        }

        public async Task<UserInfoResponse> GetUserInfoAsync(string accessToken)
        {
            OidcConfiguration oidc = await _discovery.GetAsync();

            if (oidc == null || string.IsNullOrEmpty(oidc.UserInfoEndpoint))
                return UserInfoResponse.Failed(XsollaIdErrorCode.NotConfigured, "userinfo is not declared in discovery.");

            HttpReply reply = await _http.GetAsync(oidc.UserInfoEndpoint, accessToken);

            if (!reply.IsSuccess)
            {
                return UserInfoResponse.Failed(
                    Classify(reply), Describe(reply, "userinfo"));
            }

            try
            {
                JObject o = JObject.Parse(reply.Body);

#if XSOLLA_ID_DEBUG
                // Snapshot for the debug panel: it cannot reach this JSON
                // itself, and only the chosen name leaves userinfo. Captured
                // BEFORE the sub check — a response without sub is worth
                // inspecting too.
                XsollaIdDebugClaims.Capture(o);
#endif

                string sub = ReadStringClaim(o, "sub");

                // sub is mandatory per OIDC Core. Without it the response is
                // not what it claims to be.
                if (string.IsNullOrEmpty(sub))
                {
                    return UserInfoResponse.Failed(
                        XsollaIdErrorCode.TokenExchangeFailed, "userinfo returned a response without sub.");
                }

                string rawPicture = PickPictureUrl(o);
                AvatarUrl picture = AvatarUrl.TryParse(rawPicture);

                // The claim arrived but is unusable: either the scheme is not
                // https or it is not a URL at all. Sign-in is unaffected, but it
                // is worth noticing. The URL itself is not logged — it may
                // carry a CDN token.
                if (!picture.IsValid && !string.IsNullOrEmpty(rawPicture))
                    XsollaIdHttp.LogWarning("userinfo returned a picture claim that is not an absolute https url.");

                return UserInfoResponse.Ok(
                    new XsollaUserId(sub),
                    PickDisplayName(o),
                    picture,

                    // The same two claims as in PickDisplayName, but WITHOUT
                    // picking: the expanded account card needs both values
                    // separately.
                    ReadStringClaim(o, "username"),
                    ReadStringClaim(o, "name"));
            }
            catch (Exception ex)
            {
                return UserInfoResponse.Failed(
                    XsollaIdErrorCode.TokenExchangeFailed, $"userinfo could not be parsed: {ex.Message}");
            }
        }

        public async Task RevokeAsync(string accessToken)
        {
            OidcConfiguration oidc = await _discovery.GetAsync();

            if (oidc == null || string.IsNullOrEmpty(oidc.RevocationEndpoint))
                return;

            var form = new Dictionary<string, string>
            {
                ["token"] = accessToken,
                ["client_id"] = _config.Active.ClientId
            };

            HttpReply reply = await _http.PostFormAsync(oidc.RevocationEndpoint, form);

            if (reply.IsSuccess)
                return;

            // Known issue: discovery declares revocation_endpoint at the root
            // (/oauth2/revoke), while the handler lives under /api/oauth2/ — and
            // the root returns 405. Try the other path exactly once instead of
            // losing revocation for good.
            //
            // A transport error MUST trigger the retry too. In the browser the
            // root is also blocked by CORS: /oauth2/revoke has no
            // Access-Control-Allow-Origin, so the response (with its 405) never
            // reaches us — UnityWebRequest sees ConnectionError and StatusCode
            // stays 0. A 404/405-only check would work on desktop and never on
            // WebGL. /api/oauth2/revoke has CORS configured, so the second path
            // works.
            string alternate = SwapApiPrefix(oidc.RevocationEndpoint);

            if (alternate != null &&
                (reply.IsTransportError || reply.StatusCode == 404 || reply.StatusCode == 405))
            {
                HttpReply second = await _http.PostFormAsync(alternate, form);

                if (second.IsSuccess)
                    return;

                XsollaIdHttp.LogWarning(
                    $"token revocation failed at both {XsollaIdHttp.Sanitize(oidc.RevocationEndpoint)} " +
                    $"and {XsollaIdHttp.Sanitize(alternate)}.");
                return;
            }

            XsollaIdHttp.LogWarning(Describe(reply, "token revocation"));
        }

        // /oauth2/... ↔ /api/oauth2/... Returns null if the URL has neither.
        public static string SwapApiPrefix(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (url.Contains("/api/oauth2/"))
                return url.Replace("/api/oauth2/", "/oauth2/");

            if (url.Contains("/oauth2/"))
                return url.Replace("/oauth2/", "/api/oauth2/");

            return null;
        }

        private TokenResponse ReadTokens(HttpReply reply, string what)
        {
            if (!reply.IsSuccess)
                return TokenResponse.Failed(Classify(reply), Describe(reply, what));

            try
            {
                JObject o = JObject.Parse(reply.Body);

                string access = (string)o["access_token"];

                if (string.IsNullOrEmpty(access))
                {
                    return TokenResponse.Failed(
                        XsollaIdErrorCode.TokenExchangeFailed, $"{what}: no access_token in the response.");
                }

                string refresh = (string)o["refresh_token"];

                // expires_in is seconds from "now". Convert to absolute time
                // immediately: a relative value is meaningless once saved to
                // disk.
                int expiresIn = (int?)o["expires_in"] ?? 0;
                long expiresAt = _nowUnixMs() + Math.Max(0, expiresIn) * 1000L;

                return TokenResponse.Ok(
                    new AccessToken(access),
                    string.IsNullOrEmpty(refresh) ? RefreshToken.None : new RefreshToken(refresh),
                    expiresAt,
                    (string)o["id_token"]);
            }
            catch (Exception ex)
            {
                return TokenResponse.Failed(
                    XsollaIdErrorCode.TokenExchangeFailed, $"{what}: response could not be parsed — {ex.Message}");
            }
        }

        // Transport failures and server rejections are kept apart: the UI
        // reacts to them differently, and an expired refresh token is a normal
        // outcome, not an error.
        private static XsollaIdErrorCode Classify(HttpReply reply)
        {
            if (reply.IsTransportError)
                return XsollaIdErrorCode.Network;

            // 400 invalid_grant on refresh_token means "the session is gone".
            if (reply.StatusCode == 400 && Contains(reply.Body, "invalid_grant"))
                return XsollaIdErrorCode.SessionExpired;

            if (reply.StatusCode == 401)
                return XsollaIdErrorCode.SessionExpired;

            return XsollaIdErrorCode.TokenExchangeFailed;
        }

        private static bool Contains(string body, string needle) =>
            !string.IsNullOrEmpty(body) && body.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Describe(HttpReply reply, string what) =>
            reply.IsTransportError
                ? $"{what}: {reply.TransportError}"
                : $"{what}: server responded {reply.StatusCode} ({ShortBody(reply.Body)})";

        // The error body may contain a description — but not the whole JSON in
        // the log.
        private static string ShortBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "empty response";

            return body.Length <= 200 ? body : body.Substring(0, 200) + "…";
        }

        // Claims are read strictly by type.
        //
        // (string)o[key] on a claim that arrived as an object or array throws
        // InvalidCastException — which the general catch in GetUserInfoAsync
        // turns into a failure of the WHOLE userinfo. One junk field would
        // leave the player without a name even though sub is present.
        private static string ReadStringClaim(JObject o, string key) =>
            o[key] is JValue value && value.Type == JTokenType.String ? (string)value : null;

        // Name for the UI: the first non-empty of these claims.
        //
        // Xsolla ID does NOT list preferred_username in claims_supported — it
        // is kept for other instances, at no cost. email comes last: it is a
        // fallback and must not be shown in full (see DisplayNameFormat).
        private static string PickDisplayName(JObject o)
        {
            foreach (string key in new[] { "username", "preferred_username", "name", "email" })
            {
                string value = ReadStringClaim(o, key);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }

        // Profile picture URL.
        //
        // picture is a standard OIDC Core claim, part of the profile scope we
        // already request (XsollaIdConfig.DefaultScopes), and listed in
        // production's claims_supported. The other three names are a fallback
        // for other instances, at no cost.
        //
        // Plain avatar goes LAST on purpose: some providers put a preset id
        // rather than a URL under that name. It would fail the scheme check
        // anyway, but real URLs should be tried first.
        public static string PickPictureUrl(JObject o)
        {
            foreach (string key in new[] { "picture", "picture_url", "avatar_url", "avatar" })
            {
                string value = ReadStringClaim(o, key);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }
    }
}
