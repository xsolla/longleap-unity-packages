using System.Collections.Generic;
using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class XsollaIdConfigTests
    {
        // Two distinct registrations, so it is visible which one a test touched.
        private static XsollaIdConfig Config(
            XsollaIdEnvironment environment,
            string scopes = XsollaIdConfig.DefaultScopes,
            IReadOnlyList<string> allowedGameOrigins = null) => new XsollaIdConfig(
            environment,
            new XsollaIdClientRegistration("https://id-staging.xsolla.com", "stage-id", "https://stage/cb"),
            new XsollaIdClientRegistration("https://id.xsolla.com", "prod-id", "https://prod/cb"),
            scopes,
            allowedGameOrigins);

        [TestCase(XsollaIdEnvironment.Stage, "stage-id")]
        [TestCase(XsollaIdEnvironment.Production, "prod-id")]
        public void Active_FollowsEnvironment(XsollaIdEnvironment environment, string clientId)
        {
            XsollaIdConfig config = Config(environment);

            Assert.AreEqual(clientId, config.Active.ClientId);
            Assert.IsTrue(config.IsValid);
        }

        [Test]
        public void IsInvalid_WhenTheActiveRegistrationIsMissing()
        {
            var config = new XsollaIdConfig(
                XsollaIdEnvironment.Production,
                new XsollaIdClientRegistration("https://id-staging.xsolla.com", "stage-id", "https://stage/cb"),
                null);

            Assert.IsFalse(config.IsValid, "a valid Stage must not make a Production config usable");
        }

        // An empty asset field must not turn into an empty scope list: without
        // offline there is no refresh token.
        [TestCase("")]
        [TestCase(null)]
        public void EmptyScopes_FallBackToDefault(string scopes)
        {
            Assert.AreEqual(XsollaIdConfig.DefaultScopes, Config(XsollaIdEnvironment.Stage, scopes).Scopes);
        }

        [Test]
        public void NullAllowedGameOrigins_BecomeEmpty()
        {
            XsollaIdConfig config = Config(XsollaIdEnvironment.Stage);

            Assert.IsNotNull(config.AllowedGameOrigins);
            Assert.AreEqual(0, config.AllowedGameOrigins.Count);
        }

        [Test]
        public void Registration_DiscoveryUrlTrimsTrailingSlash()
        {
            var registration = new XsollaIdClientRegistration(
                "https://id.xsolla.com/", "c", "https://example.com/cb.html");

            Assert.AreEqual("https://id.xsolla.com/.well-known/openid-configuration", registration.DiscoveryUrl);
        }

        // ---- WithRedirectUri ----

        [TestCase(XsollaIdEnvironment.Stage)]
        [TestCase(XsollaIdEnvironment.Production)]
        public void WithRedirectUri_ReplacesTheActiveRegistrationOnly(XsollaIdEnvironment environment)
        {
            bool prod = environment == XsollaIdEnvironment.Production;
            XsollaIdConfig config = Config(environment);

            XsollaIdConfig replaced = config.WithRedirectUri("http://127.0.0.1:8081/xid-callback.html");

            Assert.AreEqual("http://127.0.0.1:8081/xid-callback.html", replaced.Active.RedirectUri);

            // clientId and issuer of the active registration are carried over as is.
            Assert.AreEqual(config.Active.ClientId, replaced.Active.ClientId);
            Assert.AreEqual(config.Active.IssuerBaseUrl, replaced.Active.IssuerBaseUrl);

            // The other registration is untouched: switching the environment from the
            // menu must not carry the other address along.
            Assert.AreEqual(
                prod ? "https://stage/cb" : "https://prod/cb",
                (prod ? replaced.Stage : replaced.Production).RedirectUri);

            // The original config is immutable.
            Assert.AreEqual(prod ? "https://prod/cb" : "https://stage/cb", config.Active.RedirectUri);
        }

        [Test]
        public void WithRedirectUri_KeepsScopesAndAllowedOrigins()
        {
            string[] origins = { "http://127.0.0.1:8081" };
            XsollaIdConfig config = Config(XsollaIdEnvironment.Stage, "openid offline", origins);

            XsollaIdConfig replaced = config.WithRedirectUri("https://derived/xid-callback.html");

            Assert.AreEqual("openid offline", replaced.Scopes);
            CollectionAssert.AreEqual(origins, replaced.AllowedGameOrigins);
        }

        // Nothing derived: the config stays as is. Erasing the address would be
        // worse: sign-in would become unavailable instead of using the asset value.
        [TestCase("")]
        [TestCase(null)]
        public void WithRedirectUri_KeepsTheAssetValue_WhenNothingDerived(string derived)
        {
            XsollaIdConfig config = Config(XsollaIdEnvironment.Production);

            Assert.AreEqual("https://prod/cb", config.WithRedirectUri(derived).Active.RedirectUri);
        }
    }
}
