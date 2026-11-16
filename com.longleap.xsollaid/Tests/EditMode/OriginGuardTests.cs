using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class OriginGuardTests
    {
        private static readonly string[] Allowed =
        {
            "http://127.0.0.1:8081",
            "https://game.example.com"
        };

        // localhost: the exact mistake that breaks sign-in locally — the dev
        // server listens on 127.0.0.1, while the developer types localhost.
        // The problem names the page origin and what is allowed instead.
        [TestCase("http://localhost:8081/index.html", "localhost")]
        [TestCase("http://127.0.0.1:9000/index.html", "http://127.0.0.1:9000")]
        [TestCase("http://game.example.com/", "http://game.example.com")]
        public void IsAllowed_Rejects(string pageUrl, string pageOriginInProblem)
        {
            Assert.IsFalse(OriginGuard.IsAllowed(pageUrl, Allowed, out string problem));
            StringAssert.Contains(pageOriginInProblem, problem);
            StringAssert.Contains("127.0.0.1:8081", problem);
        }

        // (pageUrl, allowlist)
        private static readonly TestCaseData[] AllowedCases =
        {
            new TestCaseData("http://127.0.0.1:8081/index.html", Allowed)
                .SetName("IsAllowed_Passes(exact origin)"),
            new TestCaseData("https://game.example.com/some/deep/path", Allowed)
                .SetName("IsAllowed_Passes(regardless of path)"),
            // Portal build: the portal serves the game, while the callback page
            // lives on our own domain. Requiring the origins to match is wrong.
            new TestCaseData(
                    "https://portal.example.com/games/demo/index.html",
                    new[] { "https://portal.example.com", "https://game.example.com" })
                .SetName("IsAllowed_Passes(portal origin, callback elsewhere)"),
            new TestCaseData("http://127.0.0.1:8081/a", new[] { "not a url", "http://127.0.0.1:8081" })
                .SetName("IsAllowed_Passes(unparseable entries are skipped)")
        };

        [TestCaseSource(nameof(AllowedCases))]
        public void IsAllowed_Passes(string pageUrl, string[] allowlist)
        {
            Assert.IsTrue(OriginGuard.IsAllowed(pageUrl, allowlist, out string problem));
            Assert.IsNull(problem);
        }

        // No basis to block: no origin (editor, standalone), no usable allowlist
        // (an empty config must not silently break sign-in), or an unparseable
        // page address. The server decides then.
        private static readonly TestCaseData[] NotBlockingCases =
        {
            new TestCaseData("", Allowed).SetName("DoesNotBlock(empty page url)"),
            new TestCaseData(null, Allowed).SetName("DoesNotBlock(null page url)"),
            new TestCaseData("not a url", Allowed).SetName("DoesNotBlock(unparseable page url)"),
            new TestCaseData("http://example.com/", new string[0]).SetName("DoesNotBlock(empty allowlist)"),
            new TestCaseData("http://example.com/", null).SetName("DoesNotBlock(null allowlist)"),
            new TestCaseData("http://example.com/", new[] { "not a url", "also not a url" })
                .SetName("DoesNotBlock(all entries unparseable)")
        };

        [TestCaseSource(nameof(NotBlockingCases))]
        public void DoesNotBlock(string pageUrl, string[] allowlist)
        {
            Assert.IsTrue(OriginGuard.IsAllowed(pageUrl, allowlist, out string problem));
            Assert.IsNull(problem);
        }

        [Test]
        public void TryGetOrigin_OmitsDefaultPort()
        {
            Assert.AreEqual("https://example.com", OriginGuard.TryGetOrigin("https://example.com/a/b?c=d"));
            Assert.AreEqual("http://127.0.0.1:8081", OriginGuard.TryGetOrigin("http://127.0.0.1:8081/a"));
        }
    }
}
