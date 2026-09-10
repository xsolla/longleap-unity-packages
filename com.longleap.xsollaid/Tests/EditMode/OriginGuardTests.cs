using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class OriginGuardTests
    {
        private static readonly string[] Allowed =
        {
            "http://127.0.0.1:8081",
            "https://blood-red-snow.pages.dev"
        };

        [Test]
        public void LocalhostIsNotTheSameOriginAsLoopbackIp()
        {
            // Ровно та ошибка, из-за которой вход падает локально: serve.py
            // слушает 127.0.0.1, а разработчик по привычке набирает localhost.
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
                "https://blood-red-snow.pages.dev/some/deep/path", Allowed, out _));
        }

        [Test]
        public void DifferentPort_IsNotAllowed()
        {
            Assert.IsFalse(OriginGuard.IsAllowed("http://127.0.0.1:9000/index.html", Allowed, out _));
        }

        [Test]
        public void DifferentScheme_IsNotAllowed()
        {
            Assert.IsFalse(OriginGuard.IsAllowed("http://blood-red-snow.pages.dev/", Allowed, out _));
        }

        [Test]
        public void GameAndCallbackNeedNotShareAnOrigin()
        {
            // Портальная сборка: игру отдаёт GamePix, страница обратного
            // вызова лежит на нашем домене. Требовать совпадения нельзя.
            string[] withPortal = { "https://play.gamepix.com", "https://blood-red-snow.pages.dev" };

            Assert.IsTrue(OriginGuard.IsAllowed(
                "https://play.gamepix.com/games/blood-red-snow/index.html", withPortal, out _));
        }

        [Test]
        public void EmptyPageUrl_PassesBecauseThereIsNoOrigin()
        {
            // Редактор и standalone: origin'а нет, запрещать нечего.
            Assert.IsTrue(OriginGuard.IsAllowed("", Allowed, out _));
            Assert.IsTrue(OriginGuard.IsAllowed(null, Allowed, out _));
        }

        [Test]
        public void EmptyAllowlist_DoesNotBlock()
        {
            // Незаполненный конфиг не должен молча ломать вход — решает сервер.
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
