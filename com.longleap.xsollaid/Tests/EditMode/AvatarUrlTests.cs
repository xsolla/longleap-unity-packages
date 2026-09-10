using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class AvatarUrlTests
    {
        private const string Valid = "https://cdn.xsolla.com/avatars/a.png";

        [Test]
        public void TryParse_AcceptsAbsoluteHttps()
        {
            AvatarUrl url = AvatarUrl.TryParse(Valid);

            Assert.IsTrue(url.IsValid);
            Assert.AreEqual(Valid, url.Value);
        }

        [Test]
        public void TryParse_NormalisesUppercaseScheme()
        {
            // Uri приводит схему к нижнему регистру сам — проверяем, что
            // сравнение с UriSchemeHttps на это опирается, а не ломается.
            Assert.IsTrue(AvatarUrl.TryParse("HTTPS://cdn.xsolla.com/a.png").IsValid);
        }

        [Test]
        public void TryParse_TrimsSurroundingWhitespace()
        {
            AvatarUrl url = AvatarUrl.TryParse("  " + Valid + "  ");

            Assert.IsTrue(url.IsValid);
            Assert.AreEqual(Valid, url.Value, "the stored value must be ready to hand to a web request");
        }

        // Всё непригодное становится None и НЕ бросает: picture —
        // необязательный клейм, и мусор в нём не должен ронять вход.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("http://cdn.xsolla.com/a.png")]
        [TestCase("data:image/png;base64,AAAA")]
        [TestCase("javascript:alert(1)")]
        [TestCase("/relative/a.png")]
        [TestCase("cdn.xsolla.com/a.png")]
        [TestCase("not a url at all")]
        public void TryParse_RejectsAnythingButAbsoluteHttps(string raw)
        {
            Assert.IsFalse(AvatarUrl.TryParse(raw).IsValid, raw ?? "null");
        }

        [Test]
        public void None_IsInvalidAndEqualsDefault()
        {
            Assert.IsFalse(AvatarUrl.None.IsValid);
            Assert.AreEqual(default(AvatarUrl), AvatarUrl.None);
        }

        [Test]
        public void Equality_IsOrdinal()
        {
            Assert.AreEqual(AvatarUrl.TryParse(Valid), AvatarUrl.TryParse(Valid));
            Assert.AreNotEqual(AvatarUrl.TryParse(Valid), AvatarUrl.TryParse("https://cdn.xsolla.com/avatars/A.png"));
            Assert.IsTrue(AvatarUrl.TryParse(Valid) == AvatarUrl.TryParse(Valid));
            Assert.IsTrue(AvatarUrl.TryParse(Valid) != AvatarUrl.None);
        }

        // Адрес CDN может нести подписанный токен — в лог он попадать не должен.
        [Test]
        public void ToString_DoesNotLeakTheUrl()
        {
            string printed = AvatarUrl.TryParse("https://cdn.xsolla.com/a.png?token=secret-value").ToString();

            StringAssert.DoesNotContain("secret-value", printed);
            StringAssert.DoesNotContain("cdn.xsolla.com", printed);
            Assert.AreEqual("AvatarUrl(set)", printed);
            Assert.AreEqual("AvatarUrl(none)", AvatarUrl.None.ToString());
        }
    }
}
