using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaIdTokenClientTests
    {
        private const string Issuer = "https://id.xsolla.com";

        // Ровно то, что отдаёт прод: authorize/token/userinfo под /api/oauth2/,
        // а revoke — в корне. Именно это расхождение и лечит запасной путь.
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

        // Записывает КАЖДЫЙ адрес, а не только последний: весь смысл тестов —
        // сколько раз и куда именно сходили.
        private sealed class FakeTransport
        {
            private readonly Queue<HttpReply> _replies = new Queue<HttpReply>();

            public readonly List<string> Urls = new List<string>();

            public FakeTransport(params HttpReply[] replies)
            {
                foreach (HttpReply reply in replies)
                    _replies.Enqueue(reply);
            }

            public XsollaIdHttp AsHttp() => new XsollaIdHttp(call =>
            {
                Urls.Add(call.Url);

                return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : HttpReply.Status(500));
            });
        }

        private static XsollaIdConfig Config() => new XsollaIdConfig(
            XsollaIdEnvironment.Production,
            new XsollaIdClientRegistration(XsollaIdConfig.DefaultStageIssuer, "client-stage", "http://127.0.0.1:8081/xid-callback.html"),
            new XsollaIdClientRegistration(Issuer, "client-prod", "http://127.0.0.1:8081/xid-callback.html"),
            XsollaIdConfig.DefaultScopes,
            new[] { "http://127.0.0.1:8081" });

        private static XsollaIdTokenClient Create(FakeTransport transport) =>
            new XsollaIdTokenClient(Config(), new FakeDiscovery(), transport.AsHttp());

        // Регрессия. В браузере корневой /oauth2/revoke режется CORS'ом: без
        // Access-Control-Allow-Origin ответ (вместе с его 405) до нас не
        // доходит, UnityWebRequest отдаёт ConnectionError, а StatusCode
        // остаётся нулём. Пока условие смотрело только на 404/405, запасной
        // путь не пробовался никогда и отзыв токена тихо терялся.
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

        // Отказ по существу (сервер ответил и всё понял) повторять незачем:
        // второй путь лечит только «не туда постучались».
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

        // GetUserInfoAsync делает ровно один запрос, поэтому одного ответа в
        // очереди достаточно.
        private static Task<UserInfoResponse> UserInfo(string json) =>
            Create(new FakeTransport(HttpReply.Ok(json))).GetUserInfoAsync("access-1");

        [Test]
        public async Task UserInfo_PicksUsernameOverEmail()
        {
            UserInfoResponse info = await UserInfo("{\"sub\":\"s\",\"username\":\"anton\",\"email\":\"a@b.c\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("s", info.Subject.Value);
            Assert.AreEqual("anton", info.DisplayName);
        }

        [Test]
        public async Task UserInfo_FallsBackToEmail()
        {
            UserInfoResponse info = await UserInfo("{\"sub\":\"s\",\"email\":\"a@b.c\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("a@b.c", info.DisplayName);
        }

        // Регрессия. Клейм, приехавший объектом, ронял приведение
        // (string)o[key], а InvalidCastException ловил общий catch и превращал
        // ВЕСЬ userinfo в отказ — игрок оставался без имени, хотя sub на месте
        // и email рядом лежит читаемый.
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
            // sub обязателен по OIDC Core: без него ответ не тот, за который
            // себя выдаёт, и это как раз повод отказать.
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
                "{\"sub\":\"s\",\"username\":\"anton\",\"picture\":\"https://cdn.xsolla.com/a.png\"}");

            Assert.IsTrue(info.IsSuccess, info.Message);
            Assert.AreEqual("https://cdn.xsolla.com/a.png", info.Picture.Value);
        }

        // САМЫЙ ВАЖНЫЙ тест этой пачки. Отдаёт ли конкретный инстанс Xsolla ID
        // клейм picture, заранее неизвестно — вся затея обязана вырождаться в
        // «картинки нет», не задевая вход.
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

        // Непригодный адрес роняет ТОЛЬКО картинку. Вход из-за аватара
        // проваливаться не имеет права.
        [TestCase("http://cdn.xsolla.com/a.png")]
        [TestCase("data:image/png;base64,AAAA")]
        [TestCase("javascript:alert(1)")]
        [TestCase("/relative/a.png")]
        [TestCase("")]
        [TestCase("   ")]
        public async Task UserInfo_UnusablePicture_DoesNotFailSignIn(string raw)
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"username\":\"anton\",\"picture\":\"" + raw + "\"}");

            Assert.IsTrue(info.IsSuccess, raw);
            Assert.IsFalse(info.Picture.IsValid, raw);
            Assert.AreEqual("anton", info.DisplayName);
        }

        [Test]
        public async Task UserInfo_NonStringPictureClaim_DoesNotFailUserInfo()
        {
            UserInfoResponse info = await UserInfo(
                "{\"sub\":\"s\",\"username\":\"anton\",\"picture\":{\"url\":\"https://cdn.xsolla.com/a.png\"}}");

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
