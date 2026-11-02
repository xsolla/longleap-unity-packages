using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaIdAvatarLoaderTests
    {
        private const string Url = "https://cdn.xsolla.com/a.png";
        private const string OtherUrl = "https://cdn.xsolla.com/b.png";

        private FakeDownloader _downloads;

        // The loader cache is STATIC, and NUnit runs all fixtures in one domain.
        // Without resetting on both sides, tests would see each other's cache, and
        // textures would be left behind in the editor.
        [SetUp]
        public void SetUp()
        {
            XsollaIdAvatarLoader.Forget();
            _downloads = new FakeDownloader();
        }

        [TearDown]
        public void TearDown() => XsollaIdAvatarLoader.Forget();

        private sealed class FakeDownloader
        {
            private readonly Queue<TextureReply> _replies = new Queue<TextureReply>();

            public readonly List<string> Urls = new List<string>();

            // When set, the load "hangs" until the test completes the task. Needed
            // to test coalescing of concurrent requests: with an instant reply the
            // second call would happen after the first one finished.
            public TaskCompletionSource<TextureReply> Gate;

            public void Enqueue(params TextureReply[] replies)
            {
                foreach (TextureReply reply in replies)
                    _replies.Enqueue(reply);
            }

            public Task<TextureReply> Download(string url)
            {
                Urls.Add(url);

                if (Gate != null)
                    return Gate.Task;

                return Task.FromResult(
                    _replies.Count > 0 ? _replies.Dequeue() : TextureReply.Ok(NewTexture()));
            }
        }

        // 4x4 is enough: the content does not matter, only that a texture was decoded.
        private static Texture2D NewTexture() => new Texture2D(4, 4);

        private XsollaIdAvatarLoader Create() => new XsollaIdAvatarLoader(_downloads.Download);

        [Test]
        public async Task Load_Success_ReturnsASprite()
        {
            _downloads.Enqueue(TextureReply.Ok(NewTexture()));

            var ok = await Create().LoadAsync(AvatarUrl.TryParse(Url)) as AvatarResult.Success;

            Assert.IsNotNull(ok);
            Assert.IsNotNull(ok.Sprite);
            Assert.AreEqual(4, ok.Sprite.texture.width);
        }

        [Test]
        public async Task Load_SameUrlTwice_DownloadsOnceAndReturnsTheSameSprite()
        {
            XsollaIdAvatarLoader loader = Create();

            var first = await loader.LoadAsync(AvatarUrl.TryParse(Url)) as AvatarResult.Success;
            var second = await loader.LoadAsync(AvatarUrl.TryParse(Url)) as AvatarResult.Success;

            Assert.AreEqual(1, _downloads.Urls.Count, "the second call must come from the cache");
            Assert.AreSame(first.Sprite, second.Sprite, "the sprite is shared, not re-created");
        }

        // Two widgets in the same frame must not download the picture twice.
        [Test]
        public async Task Load_ConcurrentSameUrl_DownloadsOnce()
        {
            _downloads.Gate = new TaskCompletionSource<TextureReply>();

            XsollaIdAvatarLoader loader = Create();

            Task<AvatarResult> first = loader.LoadAsync(AvatarUrl.TryParse(Url));
            Task<AvatarResult> second = loader.LoadAsync(AvatarUrl.TryParse(Url));

            Assert.AreSame(first, second, "a second request must get the same task");
            Assert.AreEqual(1, _downloads.Urls.Count);

            _downloads.Gate.SetResult(TextureReply.Ok(NewTexture()));

            Assert.IsInstanceOf<AvatarResult.Success>(await first);
        }

        // This is exactly what a missing CORS header on the image host looks like:
        // the response never reaches us, StatusCode stays zero, and it is
        // indistinguishable from "no network".
        [Test]
        public async Task Load_TransportError_IsNetwork()
        {
            _downloads.Enqueue(TextureReply.Transport("GET https://cdn.xsolla.com/a.png — Unknown Error"));

            var failure = await Create().LoadAsync(AvatarUrl.TryParse(Url)) as AvatarResult.Failure;

            Assert.IsNotNull(failure);
            Assert.AreEqual(XsollaIdErrorCode.Network, failure.Code);
        }

        [TestCase(403)]
        [TestCase(404)]
        [TestCase(500)]
        public async Task Load_ServerRefusal_IsPictureUnavailable(int code)
        {
            _downloads.Enqueue(TextureReply.Status(code));

            var failure = await Create().LoadAsync(AvatarUrl.TryParse(Url)) as AvatarResult.Failure;

            Assert.IsNotNull(failure);
            Assert.AreEqual(XsollaIdErrorCode.PictureUnavailable, failure.Code);
        }

        // Regression from a live build. Xsolla ID serves avatars as webp; the bytes
        // arrive in full (CORS on cdn.xsolla.net is configured), but the decoder
        // rejects them. This is NOT a network issue: offering "retry" is pointless,
        // the same thing would happen. The transport reports such cases as 415.
        [Test]
        public async Task Load_UndecodableBody_IsPictureUnavailableNotNetwork()
        {
            _downloads.Enqueue(TextureReply.Status(415));

            var failure = await Create().LoadAsync(AvatarUrl.TryParse(Url)) as AvatarResult.Failure;

            Assert.IsNotNull(failure);
            Assert.AreEqual(
                XsollaIdErrorCode.PictureUnavailable, failure.Code,
                "a format the decoder cannot read is not a network problem");
        }

        // 200, but nothing to decode, e.g. an HTML error page came back.
        [Test]
        public async Task Load_SuccessWithoutATexture_IsPictureUnavailable()
        {
            _downloads.Enqueue(TextureReply.Status(200));

            var failure = await Create().LoadAsync(AvatarUrl.TryParse(Url)) as AvatarResult.Failure;

            Assert.IsNotNull(failure);
            Assert.AreEqual(XsollaIdErrorCode.PictureUnavailable, failure.Code);
        }

        // Failures are cached too: otherwise a dead address would be requested
        // again on every scene reload.
        [Test]
        public async Task Load_Failure_IsCachedAndNotRetried()
        {
            _downloads.Enqueue(TextureReply.Status(404));

            XsollaIdAvatarLoader loader = Create();

            await loader.LoadAsync(AvatarUrl.TryParse(Url));
            await loader.LoadAsync(AvatarUrl.TryParse(Url));

            Assert.AreEqual(1, _downloads.Urls.Count);
        }

        [Test]
        public async Task Load_InvalidUrl_TouchesNoNetwork()
        {
            var failure = await Create().LoadAsync(AvatarUrl.None) as AvatarResult.Failure;

            Assert.IsNotNull(failure);
            Assert.AreEqual(XsollaIdErrorCode.PictureUnavailable, failure.Code);
            Assert.AreEqual(0, _downloads.Urls.Count, "there is nothing to request");
        }

        [Test]
        public async Task Load_DifferentUrls_AreFetchedSeparately()
        {
            XsollaIdAvatarLoader loader = Create();

            await loader.LoadAsync(AvatarUrl.TryParse(Url));
            await loader.LoadAsync(AvatarUrl.TryParse(OtherUrl));

            Assert.AreEqual(2, _downloads.Urls.Count);
        }

        // ---- TryGetCached: the scene reload path ----

        [Test]
        public void TryGetCached_BeforeAnyLoad_IsFalse()
        {
            Assert.IsFalse(Create().TryGetCached(AvatarUrl.TryParse(Url), out Sprite sprite));
            Assert.IsNull(sprite);
        }

        [Test]
        public async Task TryGetCached_AfterLoad_ReturnsTheSameSprite()
        {
            XsollaIdAvatarLoader loader = Create();

            var ok = await loader.LoadAsync(AvatarUrl.TryParse(Url)) as AvatarResult.Success;

            // A new instance, as after a scene reload: the cache is static, so the
            // picture must be found without waiting.
            Assert.IsTrue(Create().TryGetCached(AvatarUrl.TryParse(Url), out Sprite sprite));
            Assert.AreSame(ok.Sprite, sprite);
        }

        [Test]
        public void TryGetCached_WhileInFlight_IsFalse()
        {
            _downloads.Gate = new TaskCompletionSource<TextureReply>();

            XsollaIdAvatarLoader loader = Create();

            _ = loader.LoadAsync(AvatarUrl.TryParse(Url));

            Assert.IsFalse(
                loader.TryGetCached(AvatarUrl.TryParse(Url), out Sprite sprite),
                "an unfinished download has no sprite to hand over");
            Assert.IsNull(sprite);

            _downloads.Gate.SetResult(TextureReply.Status(404));
        }

        [Test]
        public async Task TryGetCached_AfterFailure_IsFalse()
        {
            _downloads.Enqueue(TextureReply.Status(404));

            XsollaIdAvatarLoader loader = Create();

            await loader.LoadAsync(AvatarUrl.TryParse(Url));

            Assert.IsFalse(loader.TryGetCached(AvatarUrl.TryParse(Url), out Sprite _));
        }

        [Test]
        public async Task Forget_DropsTheCacheSoTheNextLoadRefetches()
        {
            XsollaIdAvatarLoader loader = Create();

            await loader.LoadAsync(AvatarUrl.TryParse(Url));

            XsollaIdAvatarLoader.Forget();

            await loader.LoadAsync(AvatarUrl.TryParse(Url));

            Assert.AreEqual(2, _downloads.Urls.Count);
        }
    }
}
