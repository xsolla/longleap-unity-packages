using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class OidcDiscoveryTests
    {
        private const string DiscoveryUrl = "https://id-staging.xsolla.com/.well-known/openid-configuration";

        // The document as the stage environment serves it: authorize/token/userinfo
        // under /api/oauth2/*, revoke at the root.
        private const string ValidDocument = @"{
            ""issuer"": ""https://id-staging.xsolla.com"",
            ""authorization_endpoint"": ""https://id-staging.xsolla.com/api/oauth2/auth"",
            ""token_endpoint"": ""https://id-staging.xsolla.com/api/oauth2/token"",
            ""userinfo_endpoint"": ""https://id-staging.xsolla.com/api/oauth2/userinfo"",
            ""revocation_endpoint"": ""https://id-staging.xsolla.com/oauth2/revoke""
        }";

        private static OidcDiscovery Create(FakeTransport transport)
        {
            // Past the queued replies the server keeps serving the valid document.
            transport.WhenEmpty = HttpReply.Ok(ValidDocument);
            return new OidcDiscovery(DiscoveryUrl, transport.AsHttp());
        }

        // ---- cache ----

        [Test]
        public async Task GetAsync_CachesSuccess()
        {
            var transport = new FakeTransport(HttpReply.Ok(ValidDocument));
            OidcDiscovery discovery = Create(transport);

            OidcConfiguration first = await discovery.GetAsync();
            OidcConfiguration second = await discovery.GetAsync();

            Assert.AreEqual(1, transport.Calls.Count, "a successful document must come from the cache");
            Assert.AreSame(first, second);
        }

        // What a failed first attempt can look like.
        private static readonly object[] FailedFirstReplies =
        {
            new object[] { "transport", HttpReply.Transport("GET " + DiscoveryUrl + " — Unknown Error") },
            new object[] { "5xx", HttpReply.Status(503) },
            new object[] { "unusable document", HttpReply.Ok(@"{""issuer"": ""https://id-staging.xsolla.com""}") }
        };

        // Regression: a failed startup warm-up (CORS, a network blip) used to be
        // cached, and every later sign-in failed with NotConfigured without
        // touching the network. A malformed document must not stick either: the
        // endpoints are being moved server-side, and the next reply may be fine.
        [TestCaseSource(nameof(FailedFirstReplies))]
        public async Task GetAsync_RetriesAfterFailedAttempt(string what, HttpReply firstReply)
        {
            var transport = new FakeTransport(firstReply, HttpReply.Ok(ValidDocument));
            OidcDiscovery discovery = Create(transport);

            OidcConfiguration failed = await discovery.GetAsync();
            OidcConfiguration retried = await discovery.GetAsync();

            Assert.IsNull(failed, what + ": the first attempt must fail");
            Assert.AreEqual(2, transport.Calls.Count, what + ": a failure must not be cached");
            Assert.IsNotNull(retried, what + ": the second attempt must return a working document");
            Assert.AreEqual("https://id-staging.xsolla.com/api/oauth2/token", retried.TokenEndpoint);
        }

        [Test]
        public async Task GetAsync_SharesOneLoadBetweenParallelCallers()
        {
            var transport = new FakeTransport { Gate = new TaskCompletionSource<HttpReply>() };
            OidcDiscovery discovery = Create(transport);

            // Both calls start while the first load is still in flight.
            Task<OidcConfiguration> a = discovery.GetAsync();
            Task<OidcConfiguration> b = discovery.GetAsync();

            Assert.AreSame(a, b, "a second caller must get the in-flight task");
            Assert.AreEqual(1, transport.Calls.Count, "parallel callers share one load");

            transport.Gate.SetResult(HttpReply.Ok(ValidDocument));

            Assert.IsNotNull(await a);
            Assert.AreSame(await a, await b);
        }

        // ---- parsing ----

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

        // userinfo and revoke are optional: sign-in works without them.
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
