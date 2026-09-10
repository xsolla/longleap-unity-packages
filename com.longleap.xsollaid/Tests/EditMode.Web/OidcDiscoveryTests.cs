using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class OidcDiscoveryTests
    {
        private const string DiscoveryUrl = "https://id-staging.xsolla.com/.well-known/openid-configuration";

        // Документ в той форме, в какой его отдаёт stage: authorize/token/
        // userinfo под /api/oauth2/*, revoke — в корне.
        private const string ValidDocument = @"{
            ""issuer"": ""https://id-staging.xsolla.com"",
            ""authorization_endpoint"": ""https://id-staging.xsolla.com/api/oauth2/auth"",
            ""token_endpoint"": ""https://id-staging.xsolla.com/api/oauth2/token"",
            ""userinfo_endpoint"": ""https://id-staging.xsolla.com/api/oauth2/userinfo"",
            ""revocation_endpoint"": ""https://id-staging.xsolla.com/oauth2/revoke""
        }";

        // Очередь заготовленных ответов: тесты про повтор должны отдать сперва
        // отказ, потом успех, и сосчитать походы в сеть.
        private sealed class FakeTransport
        {
            private readonly Queue<HttpReply> _replies = new Queue<HttpReply>();

            public int Calls;
            public string LastUrl;

            public FakeTransport Enqueue(params HttpReply[] replies)
            {
                foreach (HttpReply reply in replies)
                    _replies.Enqueue(reply);

                return this;
            }

            public XsollaIdHttp AsHttp() => new XsollaIdHttp(call =>
            {
                Calls++;
                LastUrl = call.Url;

                return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : HttpReply.Ok(ValidDocument));
            });
        }

        private static OidcDiscovery Create(FakeTransport transport) =>
            new OidcDiscovery(DiscoveryUrl, transport.AsHttp());

        // ---- кэш ----

        [Test]
        public async Task GetAsync_CachesSuccess()
        {
            var transport = new FakeTransport().Enqueue(HttpReply.Ok(ValidDocument));
            OidcDiscovery discovery = Create(transport);

            OidcConfiguration first = await discovery.GetAsync();
            OidcConfiguration second = await discovery.GetAsync();

            Assert.AreEqual(1, transport.Calls, "a successful document must come from the cache");
            Assert.AreSame(first, second);
        }

        // Регрессия. Прогрев на старте ловил CORS (или просто моргнувшую сеть),
        // LoadAsync возвращал null штатно — задача завершалась успешно, и
        // кэш отдавал этот null до конца сессии. Каждый следующий клик по
        // «войти» падал с NotConfigured, ни разу не сходив в сеть.
        [Test]
        public async Task GetAsync_RetriesAfterTransportFailure()
        {
            var transport = new FakeTransport().Enqueue(
                HttpReply.Transport("GET " + DiscoveryUrl + " — Unknown Error"),
                HttpReply.Ok(ValidDocument));
            OidcDiscovery discovery = Create(transport);

            OidcConfiguration failed = await discovery.GetAsync();
            OidcConfiguration retried = await discovery.GetAsync();

            Assert.IsNull(failed, "the first attempt must fail");
            Assert.AreEqual(2, transport.Calls, "a failure must not be cached — it must hit the network again");
            Assert.IsNotNull(retried, "the second attempt must return a working document");
            Assert.IsTrue(retried.IsUsable);
            Assert.AreEqual("https://id-staging.xsolla.com/api/oauth2/token", retried.TokenEndpoint);
        }

        [Test]
        public async Task GetAsync_RetriesAfterServerError()
        {
            var transport = new FakeTransport().Enqueue(
                HttpReply.Status(503), HttpReply.Ok(ValidDocument));
            OidcDiscovery discovery = Create(transport);

            Assert.IsNull(await discovery.GetAsync());
            Assert.IsNotNull(await discovery.GetAsync());
            Assert.AreEqual(2, transport.Calls, "5xx is a failure too, not a usable answer");
        }

        // Кривой документ так же не должен застревать в кэше: на той стороне
        // эндпоинты переезжают, и следующий ответ может быть уже нормальным.
        [Test]
        public async Task GetAsync_RetriesAfterUnusableDocument()
        {
            var transport = new FakeTransport().Enqueue(
                HttpReply.Ok(@"{""issuer"": ""https://id-staging.xsolla.com""}"),
                HttpReply.Ok(ValidDocument));
            OidcDiscovery discovery = Create(transport);

            Assert.IsNull(await discovery.GetAsync(), "without token_endpoint the document is unusable");
            Assert.IsNotNull(await discovery.GetAsync());
            Assert.AreEqual(2, transport.Calls, "an unusable document must not be cached");
        }

        [Test]
        public async Task GetAsync_SharesOneLoadBetweenParallelCallers()
        {
            var transport = new FakeTransport().Enqueue(HttpReply.Ok(ValidDocument));
            OidcDiscovery discovery = Create(transport);

            Task<OidcConfiguration> a = discovery.GetAsync();
            Task<OidcConfiguration> b = discovery.GetAsync();

            await Task.WhenAll(a, b);

            Assert.AreEqual(1, transport.Calls, "parallel callers share one load");
        }

        // ---- разбор ----

        [Test]
        public void Parse_ReadsEndpoints()
        {
            OidcConfiguration config = OidcDiscovery.Parse(ValidDocument, DiscoveryUrl);

            Assert.IsNotNull(config);
            Assert.AreEqual("https://id-staging.xsolla.com", config.Issuer);
            Assert.AreEqual("https://id-staging.xsolla.com/api/oauth2/auth", config.AuthorizationEndpoint);
            Assert.AreEqual("https://id-staging.xsolla.com/oauth2/revoke", config.RevocationEndpoint);
        }

        [Test]
        public void Parse_RejectsEndpointsOnForeignHost()
        {
            string document = ValidDocument.Replace(
                "https://id-staging.xsolla.com/api/oauth2/token", "https://evil.example.com/oauth2/token");

            Assert.IsNull(OidcDiscovery.Parse(document, DiscoveryUrl), "a foreign host would divert the authorization code");
        }

        [Test]
        public void Parse_ReturnsNullOnGarbage()
        {
            Assert.IsNull(OidcDiscovery.Parse("<!DOCTYPE html><html></html>", DiscoveryUrl));
            Assert.IsNull(OidcDiscovery.Parse("", DiscoveryUrl));
            Assert.IsNull(OidcDiscovery.Parse(null, DiscoveryUrl));
        }

        // userinfo и revoke необязательны: без них вход всё равно работает.
        [Test]
        public void Parse_AcceptsDocumentWithoutOptionalEndpoints()
        {
            OidcConfiguration config = OidcDiscovery.Parse(@"{
                ""issuer"": ""https://id-staging.xsolla.com"",
                ""authorization_endpoint"": ""https://id-staging.xsolla.com/api/oauth2/auth"",
                ""token_endpoint"": ""https://id-staging.xsolla.com/api/oauth2/token""
            }", DiscoveryUrl);

            Assert.IsNotNull(config);
            Assert.IsTrue(config.IsUsable);
            Assert.IsNull(config.UserInfoEndpoint);
        }
    }
}
