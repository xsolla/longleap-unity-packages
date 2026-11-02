using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    // Deriving redirect_uri from the game's own address.
    //
    // Covered by tests because a mistake here only shows up in a live sign-in:
    // the server compares redirect_uri character by character and rejects it
    // only AFTER the window has opened.
    [TestFixture]
    public class XsollaIdRedirectTests
    {
        // The three addresses this derivation exists for: each would otherwise
        // require editing the asset before a build.
        [TestCase("http://127.0.0.1:8081/index.html", "http://127.0.0.1:8081/xid-callback.html")]
        [TestCase("https://blood-red-snow.pages.dev/index.html", "https://blood-red-snow.pages.dev/xid-callback.html")]
        [TestCase("https://bloodredsnow.hel1.your-objectstorage.com/index.html",
            "https://bloodredsnow.hel1.your-objectstorage.com/xid-callback.html")]
        public void Derives_CallbackNextToTheGame(string pageUrl, string expected)
        {
            Assert.AreEqual(expected, XsollaIdRedirect.TryDerive(pageUrl, out string problem));

            // Nothing to report on success, otherwise the log would flag a problem
            // where everything worked.
            Assert.IsNull(problem);
        }

        // The build may be served from a subfolder; the callback page lives
        // there too, not at the domain root.
        [Test]
        public void Derives_FromTheDirectory_NotTheDomainRoot()
        {
            Assert.AreEqual(
                "https://host.example/game/xid-callback.html",
                XsollaIdRedirect.TryDerive("https://host.example/game/index.html", out _));
        }

        // An address without a file name: the directory is the address itself.
        [Test]
        public void Derives_WhenThePathIsADirectory()
        {
            Assert.AreEqual(
                "https://host.example/game/xid-callback.html",
                XsollaIdRedirect.TryDerive("https://host.example/game/", out _));
        }

        // Query and fragment must never end up in redirect_uri: the server
        // compares the string character by character.
        [Test]
        public void Derives_WithoutQueryOrFragment()
        {
            Assert.AreEqual(
                "http://127.0.0.1:8081/xid-callback.html",
                XsollaIdRedirect.TryDerive("http://127.0.0.1:8081/index.html?sid=42#anchor", out _));
        }

        // A non-default port is part of the origin and must not be lost.
        [Test]
        public void Derives_KeepsTheNonDefaultPort()
        {
            StringAssert.StartsWith(
                "http://127.0.0.1:8081/", XsollaIdRedirect.TryDerive("http://127.0.0.1:8081/", out _));
        }

        // Editor and standalone: there is no page address. null means "use the
        // asset value", and that is not a failure.
        [TestCase("")]
        [TestCase(null)]
        [TestCase("not a url")]
        [TestCase("file:///C:/build/index.html")]
        public void ReturnsNull_WhenNothingCanBeDerived(string pageUrl)
        {
            Assert.IsNull(XsollaIdRedirect.TryDerive(pageUrl, out _));
        }

        // EVERY failure must have a reason: it goes to the log, and without it
        // "took the asset value" is indistinguishable from "derived".
        [TestCase("")]
        [TestCase(null)]
        [TestCase("not a url")]
        [TestCase("file:///C:/build/index.html")]
        public void ExplainsEveryFailure(string pageUrl)
        {
            XsollaIdRedirect.TryDerive(pageUrl, out string problem);

            Assert.IsFalse(string.IsNullOrWhiteSpace(problem), "a failure with no explanation would be silent in the log");
        }

        // Different failures get different explanations: an empty address in the
        // editor is normal, file:// or a garbage string is not, and the log must
        // not mix them up.
        [Test]
        public void ExplainsFailures_Distinctly()
        {
            XsollaIdRedirect.TryDerive("", out string empty);
            XsollaIdRedirect.TryDerive("not a url", out string garbage);
            XsollaIdRedirect.TryDerive("file:///C:/build/index.html", out string scheme);

            StringAssert.Contains("editor", empty);
            StringAssert.Contains("not an absolute URL", garbage);
            StringAssert.Contains("file", scheme);

            // An unparseable address is logged in full: otherwise it is unclear what
            // exactly came from the browser.
            StringAssert.Contains("not a url", garbage);
        }

        // ---- config override ----

        [Test]
        public void WithRedirectUri_ReplacesTheActiveRegistrationOnly()
        {
            var config = new XsollaIdConfig(
                XsollaIdEnvironment.Production,
                new XsollaIdClientRegistration("https://id-staging.xsolla.com", "stage-id", "https://stage/cb"),
                new XsollaIdClientRegistration("https://id.xsolla.com", "prod-id", "https://prod/cb"));

            XsollaIdConfig replaced = config.WithRedirectUri("http://127.0.0.1:8081/xid-callback.html");

            Assert.AreEqual("http://127.0.0.1:8081/xid-callback.html", replaced.Active.RedirectUri);

            // The other registration is untouched: switching the environment from the
            // menu must not carry the other address along.
            Assert.AreEqual("https://stage/cb", replaced.Stage.RedirectUri);

            // clientId and issuer of the active registration are carried over as is.
            Assert.AreEqual("prod-id", replaced.Active.ClientId);
            Assert.AreEqual("https://id.xsolla.com", replaced.Active.IssuerBaseUrl);

            // The original config is immutable.
            Assert.AreEqual("https://prod/cb", config.Active.RedirectUri);
        }

        [Test]
        public void WithRedirectUri_OnStageEnvironment_ReplacesStage()
        {
            var config = new XsollaIdConfig(
                XsollaIdEnvironment.Stage,
                new XsollaIdClientRegistration("https://id-staging.xsolla.com", "stage-id", "https://stage/cb"),
                new XsollaIdClientRegistration("https://id.xsolla.com", "prod-id", "https://prod/cb"));

            XsollaIdConfig replaced = config.WithRedirectUri("https://derived/xid-callback.html");

            Assert.AreEqual("https://derived/xid-callback.html", replaced.Stage.RedirectUri);
            Assert.AreEqual("https://prod/cb", replaced.Production.RedirectUri);
        }

        // Nothing derived: the config stays as is. Erasing the address would be
        // worse: sign-in would become unavailable instead of using the asset value.
        [TestCase("")]
        [TestCase(null)]
        public void WithRedirectUri_KeepsTheAssetValue_WhenNothingDerived(string derived)
        {
            var config = new XsollaIdConfig(
                XsollaIdEnvironment.Production,
                new XsollaIdClientRegistration("https://id-staging.xsolla.com", "stage-id", "https://stage/cb"),
                new XsollaIdClientRegistration("https://id.xsolla.com", "prod-id", "https://prod/cb"));

            Assert.AreEqual("https://prod/cb", config.WithRedirectUri(derived).Active.RedirectUri);
        }

        // The derived address must point to the same file the post-build step puts
        // into the build: if they diverge, the sign-in window shows a 404.
        [Test]
        public void CallbackFileName_MatchesWhatIsDerived()
        {
            StringAssert.EndsWith(
                XsollaIdRedirect.CallbackFileName,
                XsollaIdRedirect.TryDerive("https://host.example/index.html", out _));
        }
    }
}
