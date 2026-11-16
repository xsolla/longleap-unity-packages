using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaIdTokenClientTests
    {
        private const string Issuer = "https://id.xsolla.com";
        private const string TokenEndpoint = Issuer + "/api/oauth2/token";
        private const long Now = 1_760_000_000_000;

        // Exactly what production serves: authorize/token/userinfo under
        // /api/oauth2/, revoke at the root. The fallback path fixes this mismatch.
        private const string AdvertisedRevoke = Issuer + "/oauth2/revoke";
        private const string AlternateRevoke = Issuer + "/api/oauth2/revoke";

        private sealed class FakeDiscovery : IOidcDiscovery
        {
            public Task<OidcConfiguration> GetAsync() => Task.FromResult(new OidcConfiguration(
                Issuer,
                Issuer + "/api/oauth2/auth",
                TokenEndpoint,
                Issuer + "/api/oauth2/userinfo",
                AdvertisedRevoke));
        }

        // Discovery that failed to load.
        private sealed class NoDiscovery : IOidcDiscovery
        {
            public Task<OidcConfiguration> GetAsync() => Task.FromResult<OidcConfiguration>(null);
        }

        private static XsollaIdConfig Config() => new XsollaIdConfig(
            XsollaIdEnvironment.Production,
            new XsollaIdClientRegistration(XsollaIdConfig.DefaultStageIssuer, "client-stage", "http://127.0.0.1:8081/xid-callback.html"),
            new XsollaIdClientRegistration(Issuer, "client-prod", "http://127.0.0.1:8081/xid-callback.html"),
            XsollaIdConfig.DefaultScopes,
            new[] { "http://127.0.0.1:8081" });

        private static XsollaIdTokenClient Create(FakeTransport transport) =>
            new XsollaIdTokenClient(Config(), new FakeDiscovery(), transport.AsHttp(), () => Now);

        // Decodes the form body back into fields.
        private static Dictionary<string, string> Form(HttpCall call)
        {
            var form = new Dictionary<string, string>();

            foreach (string pair in call.Body.Split('&'))
            {
                string[] parts = pair.Split('=');
                form[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }

            return form;
        }

        // ---- code exchange / refresh ----

        [Test]
        public async Task ExchangeCode_PostsThePkceForm()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"access_token\":\"a\"}"));

            await Create(transport).ExchangeCodeAsync("code-1", "verifier-1", "http://127.0.0.1:8081/cb");

            HttpCall call = transport.Calls[0];
            Assert.AreEqual("POST", call.Method);
            Assert.AreEqual(TokenEndpoint, call.Url);
            Assert.AreEqual("application/x-www-form-urlencoded", call.ContentType);

            Dictionary<string, string> form = Form(call);
            Assert.AreEqual("authorization_code", form["grant_type"]);
            Assert.AreEqual("code-1", form["code"]);
            Assert.AreEqual("verifier-1", form["code_verifier"]);
            Assert.AreEqual("http://127.0.0.1:8081/cb", form["redirect_uri"]);
            Assert.AreEqual("client-prod", form["client_id"], "the active registration's client");
            Assert.AreEqual(5, form.Count, "a public client sends no secret");
        }

        [Test]
        public async Task Refresh_PostsTheRefreshForm()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"access_token\":\"a\"}"));

            await Create(transport).RefreshAsync("refresh-1");

            HttpCall call = transport.Calls[0];
            Assert.AreEqual(TokenEndpoint, call.Url);

            Dictionary<string, string> form = Form(call);
            Assert.AreEqual("refresh_token", form["grant_type"]);
            Assert.AreEqual("refresh-1", form["refresh_token"]);
            Assert.AreEqual("client-prod", form["client_id"]);
            Assert.AreEqual(3, form.Count);
        }

        [Test]
        public async Task ReadTokens_ConvertsExpiresInWithTheInjectedClock()
        {
            var transport = new FakeTransport(HttpReply.Ok(
                "{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":3600,\"id_token\":\"id\"}"));

            TokenResponse tokens = await Create(transport).RefreshAsync("refresh-1");

            Assert.IsTrue(tokens.IsSuccess, tokens.Message);
            Assert.AreEqual("a", tokens.Access.Value);
            Assert.AreEqual("r", tokens.Refresh.Value);
            Assert.AreEqual(Now + 3600 * 1000L, tokens.ExpiresAtUnixMs);
            Assert.AreEqual("id", tokens.RawIdToken);
        }

        [Test]
        public async Task ReadTokens_NoRefreshToken_IsNone()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"access_token\":\"a\"}"));

            TokenResponse tokens = await Create(transport).RefreshAsync("refresh-1");

            Assert.IsTrue(tokens.IsSuccess, tokens.Message);
            Assert.AreEqual(RefreshToken.None, tokens.Refresh);
            Assert.AreEqual(Now, tokens.ExpiresAtUnixMs, "no expires_in means already expired");
        }

        [TestCase("{\"refresh_token\":\"r\"}")]
        [TestCase("{\"access_token\":\"\"}")]
        [TestCase("<html>oops</html>")]
        public async Task ReadTokens_Fails(string body)
        {
            var transport = new FakeTransport(HttpReply.Ok(body));

            TokenResponse tokens = await Create(transport).ExchangeCodeAsync("c", "v", "r");

            Assert.IsFalse(tokens.IsSuccess);
            Assert.AreEqual(XsollaIdErrorCode.TokenExchangeFailed, tokens.Error);
        }

        // 0 stands for a transport error.
        [TestCase(0, null, XsollaIdErrorCode.Network)]
        [TestCase(400, "{\"error\":\"invalid_grant\"}", XsollaIdErrorCode.SessionExpired)]
        [TestCase(400, "{\"error\":\"invalid_request\"}", XsollaIdErrorCode.TokenExchangeFailed)]
        [TestCase(401, null, XsollaIdErrorCode.SessionExpired)]
        [TestCase(403, null, XsollaIdErrorCode.TokenExchangeFailed)]
        [TestCase(500, "{\"error\":\"invalid_grant\"}", XsollaIdErrorCode.TokenExchangeFailed)]
        public async Task Refresh_ClassifiesFailures(int status, string body, XsollaIdErrorCode expected)
        {
            var transport = new FakeTransport(status == 0
                ? HttpReply.Transport("POST token — Unknown Error")
                : HttpReply.Status(status, body));

            TokenResponse tokens = await Create(transport).RefreshAsync("refresh-1");

            Assert.IsFalse(tokens.IsSuccess);
            Assert.AreEqual(expected, tokens.Error);
            Assert.IsFalse(tokens.Access.IsValid);
        }

        [Test]
        public async Task TokenCalls_WithoutDiscovery_AreNotConfigured()
        {
            var transport = new FakeTransport();
            var client = new XsollaIdTokenClient(Config(), new NoDiscovery(), transport.AsHttp());

            TokenResponse exchanged = await client.ExchangeCodeAsync("c", "v", "r");
            TokenResponse refreshed = await client.RefreshAsync("refresh-1");
            UserInfoResponse info = await client.GetUserInfoAsync("access-1");

            Assert.AreEqual(XsollaIdErrorCode.NotConfigured, exchanged.Error);
            Assert.AreEqual(XsollaIdErrorCode.NotConfigured, refreshed.Error);
            Assert.AreEqual(XsollaIdErrorCode.NotConfigured, info.Error);
            Assert.AreEqual(0, transport.Calls.Count, "nowhere to send the request");
        }

        // ---- revoke ----

        // Regression: in the browser the root /oauth2/revoke is blocked by CORS,
        // so its 405 never arrives — only a transport error with status 0. The
        // fallback path must be tried for that too, or the revoke is silently lost.
        // 0 stands for that transport error.
        [TestCase(0)]
        [TestCase(404)]
        [TestCase(405)]
        public async Task Revoke_FallsBackToAlternatePath(int status)
        {
            var transport = new FakeTransport(
                status == 0
                    ? HttpReply.Transport("POST " + AdvertisedRevoke + " — Unknown Error")
                    : HttpReply.Status(status),
                HttpReply.Ok(""));

            await Create(transport).RevokeAsync("access-1");

            Assert.AreEqual(2, transport.Urls.Count, "the wrong door must not lose the revoke");
            Assert.AreEqual(AdvertisedRevoke, transport.Urls[0]);
            Assert.AreEqual(AlternateRevoke, transport.Urls[1]);
        }

        [Test]
        public async Task Revoke_StopsAtFirstSuccess()
        {
            var transport = new FakeTransport(HttpReply.Ok(""));

            await Create(transport).RevokeAsync("access-1");

            Assert.AreEqual(1, transport.Urls.Count, "a successful revoke must not be repeated");
        }

        // A real rejection (the server answered and understood the request) is not
        // worth retrying: the second path only fixes "knocked on the wrong door".
        [Test]
        public async Task Revoke_DoesNotRetry_OnServerRefusal()
        {
            var transport = new FakeTransport(HttpReply.Status(400, "{\"error\":\"invalid_token\"}"));

            await Create(transport).RevokeAsync("access-1");

            Assert.AreEqual(1, transport.Urls.Count, "a 400 is an answer, not a wrong address");
        }

        [Test]
        public void SwapApiPrefix_MapsBothDirections()
        {
            Assert.AreEqual(AlternateRevoke, XsollaIdTokenClient.SwapApiPrefix(AdvertisedRevoke));
            Assert.AreEqual(AdvertisedRevoke, XsollaIdTokenClient.SwapApiPrefix(AlternateRevoke));
            Assert.IsNull(XsollaIdTokenClient.SwapApiPrefix(Issuer + "/revoke"), "no oauth2 segment, no alternate");
            Assert.IsNull(XsollaIdTokenClient.SwapApiPrefix(null));
        }

        // ---- userinfo ----

        // GetUserInfoAsync makes exactly one request, so one queued reply is enough.
        private static Task<UserInfoResponse> UserInfo(string json) =>
            Create(new FakeTransport(HttpReply.Ok(json))).GetUserInfoAsync("access-1");

        [Test]
        public async Task UserInfo_SendsTheAccessTokenAsBearer()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"sub\":\"s\"}"));

            await Create(transport).GetUserInfoAsync("access-1");

            Assert.AreEqual("GET", transport.Calls[0].Method);
            Assert.AreEqual(Issuer + "/api/oauth2/userinfo", transport.Calls[0].Url);
            Assert.AreEqual("access-1", transport.Calls[0].BearerToken);
        }

        [Test]
        public async Task UserInfo_ReadsSubjectUserNameAndFullName()
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"username\":\"id123\",\"name\":\"Anna Smith\",\"email\":\"a@b.c\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("s", info.Subject.Value);
            Assert.AreEqual("id123", info.UserName);
            Assert.AreEqual("Anna Smith", info.FullName);
        }

        // Precedence: username > preferred_username > name > email. A missing
        // picture claim costs nothing. Claims that
        // are empty or of the wrong JSON type are skipped, not fatal.
        // Regression: a non-string claim used to fail the whole userinfo.
        [TestCase("{\"sub\":\"s\",\"username\":\"user\",\"preferred_username\":\"pref\",\"name\":\"N\",\"email\":\"a@b.c\"}", "user")]
        [TestCase("{\"sub\":\"s\",\"preferred_username\":\"pref\",\"name\":\"N\",\"email\":\"a@b.c\"}", "pref")]
        [TestCase("{\"sub\":\"s\",\"name\":\"N\",\"email\":\"a@b.c\"}", "N")]
        [TestCase("{\"sub\":\"s\",\"email\":\"a@b.c\"}", "a@b.c")]
        [TestCase("{\"sub\":\"s\",\"username\":\"\",\"email\":\"a@b.c\"}", "a@b.c")]
        [TestCase("{\"sub\":\"s\",\"name\":{\"first\":\"A\"},\"email\":\"a@b.c\"}", "a@b.c")]
        [TestCase("{\"sub\":\"s\",\"username\":42,\"name\":\"N\"}", "N")]
        [TestCase("{\"sub\":\"s\"}", null)]
        public async Task UserInfo_DisplayName(string json, string expected)
        {
            UserInfoResponse info = await UserInfo(json);

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual(expected, info.DisplayName);
            Assert.IsFalse(info.Picture.IsValid, "no picture claim means no picture, not a failure");
        }

        [Test]
        public async Task UserInfo_NonStringSub_Fails()
        {
            // sub is mandatory per OIDC Core: without it the response is not what it
            // claims to be, which is exactly a reason to reject it.
            UserInfoResponse info = await UserInfo("{\"sub\":{\"id\":1},\"email\":\"a@b.c\"}");

            Assert.IsFalse(info.IsSuccess);
            Assert.AreEqual(XsollaIdErrorCode.TokenExchangeFailed, info.Error);
        }

        // ---- userinfo: picture ----

        [TestCase("picture")]
        [TestCase("picture_url")]
        [TestCase("avatar_url")]
        [TestCase("avatar")]
        public async Task UserInfo_AcceptsPictureAliases(string claim)
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"" + claim + "\":\"https://cdn.xsolla.com/a.png\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("https://cdn.xsolla.com/a.png", info.Picture.Value, claim);
        }

        [Test]
        public async Task UserInfo_PrefersTheStandardPictureClaim()
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"avatar\":\"https://cdn.xsolla.com/preset.png\"," +
                "\"picture\":\"https://cdn.xsolla.com/real.png\"}");

            Assert.AreEqual("https://cdn.xsolla.com/real.png", info.Picture.Value);
        }

        // An unusable picture drops ONLY the picture. Sign-in must never fail
        // because of an avatar.
        [TestCase("\"http://cdn.xsolla.com/a.png\"")]
        [TestCase("\"data:image/png;base64,AAAA\"")]
        [TestCase("\"javascript:alert(1)\"")]
        [TestCase("\"/relative/a.png\"")]
        [TestCase("\"\"")]
        [TestCase("\"   \"")]
        [TestCase("{\"url\":\"https://cdn.xsolla.com/a.png\"}")]
        public async Task UserInfo_UnusablePicture_DoesNotFailSignIn(string pictureJson)
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"username\":\"user\",\"picture\":" + pictureJson + "}");

            Assert.IsTrue(info.IsSuccess, pictureJson);
            Assert.IsFalse(info.Picture.IsValid, pictureJson);
            Assert.AreEqual("user", info.DisplayName);
        }

        [Test]
        public async Task UserInfo_TransportError_IsNetwork()
        {
            var transport = new FakeTransport(HttpReply.Transport("GET userinfo — Unknown Error"));

            UserInfoResponse info = await Create(transport).GetUserInfoAsync("access-1");

            Assert.IsFalse(info.IsSuccess);
            Assert.AreEqual(XsollaIdErrorCode.Network, info.Error);
        }
    }
}
