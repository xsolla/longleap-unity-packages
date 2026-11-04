using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaIdTokenClientTests
    {
        private const string Issuer = "https://id.xsolla.com";

        // Exactly what production serves: authorize/token/userinfo under
        // /api/oauth2/, revoke at the root. The fallback path fixes this mismatch.
        private const string AdvertisedRevoke = Issuer + "/oauth2/revoke";
        private const string AlternateRevoke = Issuer + "/api/oauth2/revoke";

        private sealed class FakeDiscovery : IOidcDiscovery
        {
            public Task<OidcConfiguration> GetAsync() => Task.FromResult(new OidcConfiguration(
                Issuer,
                Issuer + "/api/oauth2/auth",
                Issuer + "/api/oauth2/token",
                Issuer + "/api/oauth2/userinfo",
                AdvertisedRevoke));
        }

        private static XsollaIdConfig Config() => new XsollaIdConfig(
            XsollaIdEnvironment.Production,
            new XsollaIdClientRegistration(XsollaIdConfig.DefaultStageIssuer, "client-stage", "http://127.0.0.1:8081/xid-callback.html"),
            new XsollaIdClientRegistration(Issuer, "client-prod", "http://127.0.0.1:8081/xid-callback.html"),
            XsollaIdConfig.DefaultScopes,
            new[] { "http://127.0.0.1:8081" });

        private static XsollaIdTokenClient Create(FakeTransport transport) =>
            new XsollaIdTokenClient(Config(), new FakeDiscovery(), transport.AsHttp());

        // Regression: in the browser the root /oauth2/revoke is blocked by CORS,
        // so its 405 never arrives — only a transport error with status 0. The
        // fallback path must be tried for that too, or the revoke is silently lost.
        [Test]
        public async Task Revoke_FallsBackToAlternatePath_OnTransportError()
        {
            var transport = new FakeTransport(
                HttpReply.Transport("POST " + AdvertisedRevoke + " — Unknown Error"),
                HttpReply.Ok(""));

            await Create(transport).RevokeAsync("access-1");

            Assert.AreEqual(2, transport.Urls.Count, "a CORS-blocked revoke must still try the alternate path");
            Assert.AreEqual(AdvertisedRevoke, transport.Urls[0]);
            Assert.AreEqual(AlternateRevoke, transport.Urls[1]);
        }

        [Test]
        public async Task Revoke_FallsBackToAlternatePath_On405()
        {
            var transport = new FakeTransport(HttpReply.Status(405), HttpReply.Ok(""));

            await Create(transport).RevokeAsync("access-1");

            Assert.AreEqual(2, transport.Urls.Count);
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
        }

        // ---- userinfo ----

        // GetUserInfoAsync makes exactly one request, so one queued reply is enough.
        private static Task<UserInfoResponse> UserInfo(string json) =>
            Create(new FakeTransport(HttpReply.Ok(json))).GetUserInfoAsync("access-1");

        [Test]
        public async Task UserInfo_PicksUsernameOverEmail()
        {
            UserInfoResponse info = await UserInfo("{\"sub\":\"s\",\"username\":\"user\",\"email\":\"a@b.c\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("s", info.Subject.Value);
            Assert.AreEqual("user", info.DisplayName);
        }

        [Test]
        public async Task UserInfo_FallsBackToEmail()
        {
            UserInfoResponse info = await UserInfo("{\"sub\":\"s\",\"email\":\"a@b.c\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("a@b.c", info.DisplayName);
        }

        // Regression: a claim of the wrong JSON type used to fail the whole
        // userinfo, leaving the player without a name although sub and a readable
        // email were there.
        [Test]
        public async Task UserInfo_NonStringNameClaim_DoesNotFailTheWholeResponse()
        {
            UserInfoResponse info = await UserInfo("{\"sub\":\"s\",\"name\":{\"first\":\"A\"},\"email\":\"a@b.c\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("a@b.c", info.DisplayName, "an unreadable claim is skipped, not fatal");
        }

        [Test]
        public async Task UserInfo_NonStringSub_Fails()
        {
            // sub is mandatory per OIDC Core: without it the response is not what it
            // claims to be, which is exactly a reason to reject it.
            UserInfoResponse info = await UserInfo("{\"sub\":{\"id\":1},\"email\":\"a@b.c\"}");

            Assert.IsFalse(info.IsSuccess);
        }

        [Test]
        public async Task UserInfo_NoNameClaims_SucceedsWithoutAName()
        {
            UserInfoResponse info = await UserInfo("{\"sub\":\"s\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.IsNull(info.DisplayName);
        }

        // ---- userinfo: picture ----

        [Test]
        public async Task UserInfo_ReadsPictureClaim()
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"username\":\"user\",\"picture\":\"https://cdn.xsolla.com/a.png\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("https://cdn.xsolla.com/a.png", info.Picture.Value);
        }

        // Whether an Xsolla ID instance returns the picture claim is not known in
        // advance: without it the feature degrades to "no picture", and sign-in is
        // unaffected.
        [Test]
        public async Task UserInfo_NoPictureClaim_StillSucceeds()
        {
            UserInfoResponse info = await UserInfo("{\"sub\":\"s\",\"email\":\"a@b.c\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.IsFalse(info.Picture.IsValid);
            Assert.AreEqual("a@b.c", info.DisplayName, "a missing picture must not cost us the name either");
        }

        [TestCase("picture")]
        [TestCase("picture_url")]
        [TestCase("avatar_url")]
        [TestCase("avatar")]
        public async Task UserInfo_AcceptsPictureAliases(string claim)
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"" + claim + "\":\"https://cdn.xsolla.com/a.png\"}");

            Assert.IsTrue(info.Picture.IsValid, claim);
        }

        [Test]
        public async Task UserInfo_PrefersTheStandardPictureClaim()
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"avatar\":\"https://cdn.xsolla.com/preset.png\"," +
                "\"picture\":\"https://cdn.xsolla.com/real.png\"}");

            Assert.AreEqual("https://cdn.xsolla.com/real.png", info.Picture.Value);
        }

        // An unusable address drops ONLY the picture. Sign-in must never fail
        // because of an avatar.
        [TestCase("http://cdn.xsolla.com/a.png")]
        [TestCase("data:image/png;base64,AAAA")]
        [TestCase("javascript:alert(1)")]
        [TestCase("/relative/a.png")]
        [TestCase("")]
        [TestCase("   ")]
        public async Task UserInfo_UnusablePicture_DoesNotFailSignIn(string raw)
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"username\":\"user\",\"picture\":\"" + raw + "\"}");

            Assert.IsTrue(info.IsSuccess, raw);
            Assert.IsFalse(info.Picture.IsValid, raw);
            Assert.AreEqual("user", info.DisplayName);
        }

        [Test]
        public async Task UserInfo_NonStringPictureClaim_DoesNotFailUserInfo()
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"username\":\"user\",\"picture\":{\"url\":\"https://cdn.xsolla.com/a.png\"}}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.IsFalse(info.Picture.IsValid);
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
