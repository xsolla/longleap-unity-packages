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
        // The three hosts this derivation exists for (each would otherwise need
        // an asset edit before a build), plus the path shapes that must not
        // break it.
        [TestCase("http://127.0.0.1:8081/index.html", "http://127.0.0.1:8081/xid-callback.html")]
        [TestCase("https://game.example.com/index.html", "https://game.example.com/xid-callback.html")]
        [TestCase("https://bucket.storage.example.com/index.html",
            "https://bucket.storage.example.com/xid-callback.html")]
        // A subfolder build: the callback lives there too, not at the domain root.
        [TestCase("https://host.example/game/index.html", "https://host.example/game/xid-callback.html")]
        // An address without a file name: the directory is the address itself.
        [TestCase("https://host.example/game/", "https://host.example/game/xid-callback.html")]
        // Query and fragment must never end up in redirect_uri.
        [TestCase("http://127.0.0.1:8081/index.html?sid=42#anchor", "http://127.0.0.1:8081/xid-callback.html")]
        public void Derives(string pageUrl, string expected)
        {
            Assert.AreEqual(expected, XsollaIdRedirect.TryDerive(pageUrl, out string problem));

            // Nothing to report on success, otherwise the log would flag a problem
            // where everything worked.
            Assert.IsNull(problem);
        }

        // null means "use the asset value" (normal in the editor), but EVERY
        // failure must have a reason: without it "took the asset value" is
        // indistinguishable from "derived" in the log.
        [TestCase("")]
        [TestCase(null)]
        [TestCase("not a url")]
        [TestCase("file:///C:/build/index.html")]
        public void Fails_WithAReason(string pageUrl)
        {
            Assert.IsNull(XsollaIdRedirect.TryDerive(pageUrl, out string problem));
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
    }
}
