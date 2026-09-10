using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace LongLeap.XsollaId.Tests
{
    // Тип небольшой, но именно его получает игровой UI, когда вход выключен
    // дефайном или собрана портальная версия — то есть в редакторе он работает
    // ВСЕГДА.
    [TestFixture]
    public class UnavailableXsollaIdAvatarLoaderTests
    {
        private UnavailableXsollaIdAvatarLoader _loader;

        [SetUp]
        public void SetUp() => _loader = new UnavailableXsollaIdAvatarLoader();

        [Test]
        public void IsAvailable_IsFalse()
        {
            Assert.IsFalse(_loader.IsAvailable);
        }

        [Test]
        public async Task LoadAsync_AlwaysFailsWithUnavailable()
        {
            var none = await _loader.LoadAsync(AvatarUrl.None) as AvatarResult.Failure;
            var valid = await _loader.LoadAsync(AvatarUrl.TryParse("https://cdn.xsolla.com/a.png"))
                as AvatarResult.Failure;

            Assert.IsNotNull(none);
            Assert.AreEqual(XsollaIdErrorCode.Unavailable, none.Code);

            Assert.IsNotNull(valid, "even a perfectly good url must not be fetched here");
            Assert.AreEqual(XsollaIdErrorCode.Unavailable, valid.Code);
        }

        [Test]
        public void TryGetCached_ReturnsFalseAndNull()
        {
            Assert.IsFalse(_loader.TryGetCached(AvatarUrl.TryParse("https://cdn.xsolla.com/a.png"), out Sprite sprite));
            Assert.IsNull(sprite);
        }
    }
}
