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

        [Test]
        public void LocalhostIsNotTheSameOriginAsLoopbackIp()
        {
            // The exact mistake that breaks sign-in locally: the dev server listens
            // on 127.0.0.1, while the developer types localhost out of habit.
            bool ok = OriginGuard.IsAllowed(
                "http://localhost:8081/index.html", Allowed, out string problem);

            Assert.IsFalse(ok);
            StringAssert.Contains("localhost", problem);
            StringAssert.Contains("127.0.0.1", problem);
        }

        [Test]
        public void AllowedOrigin_PassesRegardlessOfPath()
        {
            Assert.IsTrue(OriginGuard.IsAllowed(
                "http://127.0.0.1:8081/index.html", Allowed, out _));

            Assert.IsTrue(OriginGuard.IsAllowed(
                "https://game.example.com/some/deep/path", Allowed, out _));
        }

        [Test]
        public void DifferentPort_IsNotAllowed()
        {
            Assert.IsFalse(OriginGuard.IsAllowed("http://127.0.0.1:9000/index.html", Allowed, out _));
        }

        [Test]
        public void DifferentScheme_IsNotAllowed()
        {
            Assert.IsFalse(OriginGuard.IsAllowed("http://game.example.com/", Allowed, out _));
        }

        [Test]
        public void GameAndCallbackNeedNotShareAnOrigin()
        {
            // Portal build: the portal serves the game, while the callback page
            // lives on our own domain. Requiring the origins to match is wrong.
            string[] withPortal = { "https://portal.example.com", "https://game.example.com" };

            Assert.IsTrue(OriginGuard.IsAllowed(
                "https://portal.example.com/games/demo/index.html", withPortal, out _));
        }

        [Test]
        public void EmptyPageUrl_PassesBecauseThereIsNoOrigin()
        {
            // Editor and standalone: there is no origin, so nothing to block.
            Assert.IsTrue(OriginGuard.IsAllowed("", Allowed, out _));
            Assert.IsTrue(OriginGuard.IsAllowed(null, Allowed, out _));
        }

        [Test]
        public void EmptyAllowlist_DoesNotBlock()
        {
            // An empty config must not silently break sign-in; let the server decide.
            Assert.IsTrue(OriginGuard.IsAllowed("http://example.com/", new string[0], out _));
            Assert.IsTrue(OriginGuard.IsAllowed("http://example.com/", null, out _));
        }

        [Test]
        public void UnparseableEntries_AreSkippedNotFatal()
        {
            string[] messy = { "not a url", "http://127.0.0.1:8081" };

            Assert.IsTrue(OriginGuard.IsAllowed("http://127.0.0.1:8081/a", messy, out _));
        }

        [Test]
        public void AllEntriesUnparseable_BehavesLikeNoAllowlist()
        {
            string[] junk = { "not a url", "also not a url" };

            Assert.IsTrue(OriginGuard.IsAllowed("http://example.com/", junk, out _));
        }

        [Test]
        public void UnparseablePageUrl_DoesNotBlock()
        {
            Assert.IsTrue(OriginGuard.IsAllowed("not a url", Allowed, out _));
        }

        [Test]
        public void TryGetOrigin_OmitsDefaultPort()
        {
            Assert.AreEqual("https://example.com", OriginGuard.TryGetOrigin("https://example.com/a/b?c=d"));
            Assert.AreEqual("http://127.0.0.1:8081", OriginGuard.TryGetOrigin("http://127.0.0.1:8081/a"));
        }
    }
}
