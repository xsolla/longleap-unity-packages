using System;
using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    // AccessToken, RefreshToken, XsollaUserId and AvatarUrl.
    [TestFixture]
    public class ValueTypesTests
    {
        private const string Valid = "https://cdn.xsolla.com/avatars/a.png";

        // A token in the log is a ready-made way into someone else's account; a
        // CDN url may carry a signed token; sub links records across services.
        private static readonly TestCaseData[] Printable =
        {
            new TestCaseData(new AccessToken("eyJhbGciOiJSUzI1NiJ9.secret-payload.sig"), "secret-payload")
                .SetName("ToString_DoesNotLeak(AccessToken)"),
            new TestCaseData(new RefreshToken("refresh-super-secret"), "refresh-super-secret")
                .SetName("ToString_DoesNotLeak(RefreshToken)"),
            new TestCaseData(new XsollaUserId("sub-42"), "sub-42")
                .SetName("ToString_DoesNotLeak(XsollaUserId)"),
            new TestCaseData(AvatarUrl.TryParse("https://cdn.xsolla.com/a.png?token=secret-value"), "secret-value")
                .SetName("ToString_DoesNotLeak(AvatarUrl token)"),
            new TestCaseData(AvatarUrl.TryParse("https://cdn.xsolla.com/a.png?token=secret-value"), "cdn.xsolla.com")
                .SetName("ToString_DoesNotLeak(AvatarUrl host)")
        };

        [TestCaseSource(nameof(Printable))]
        public void ToString_DoesNotLeak(object value, string secret)
        {
            StringAssert.DoesNotContain(secret, value.ToString());
        }

        // (None, default, None.IsValid)
        private static readonly TestCaseData[] Nones =
        {
            new TestCaseData(AccessToken.None, default(AccessToken), AccessToken.None.IsValid)
                .SetName("None_IsInvalidAndEqualsDefault(AccessToken)"),
            new TestCaseData(RefreshToken.None, default(RefreshToken), RefreshToken.None.IsValid)
                .SetName("None_IsInvalidAndEqualsDefault(RefreshToken)"),
            new TestCaseData(XsollaUserId.None, default(XsollaUserId), XsollaUserId.None.IsValid)
                .SetName("None_IsInvalidAndEqualsDefault(XsollaUserId)"),
            new TestCaseData(AvatarUrl.None, default(AvatarUrl), AvatarUrl.None.IsValid)
                .SetName("None_IsInvalidAndEqualsDefault(AvatarUrl)")
        };

        [TestCaseSource(nameof(Nones))]
        public void None_IsInvalidAndEqualsDefault(object none, object @default, bool noneIsValid)
        {
            Assert.IsFalse(noneIsValid);
            Assert.AreEqual(@default, none);
        }

        // (value, same text, differs only in case)
        private static readonly TestCaseData[] Ordinal =
        {
            new TestCaseData(new AccessToken("abc"), new AccessToken("abc"), new AccessToken("ABC"))
                .SetName("Equality_IsOrdinal(AccessToken)"),
            new TestCaseData(new RefreshToken("abc"), new RefreshToken("abc"), new RefreshToken("ABC"))
                .SetName("Equality_IsOrdinal(RefreshToken)"),
            new TestCaseData(new XsollaUserId("abc"), new XsollaUserId("abc"), new XsollaUserId("ABC"))
                .SetName("Equality_IsOrdinal(XsollaUserId)"),
            new TestCaseData(
                    AvatarUrl.TryParse(Valid), AvatarUrl.TryParse(Valid),
                    AvatarUrl.TryParse("https://cdn.xsolla.com/avatars/A.png"))
                .SetName("Equality_IsOrdinal(AvatarUrl)")
        };

        [TestCaseSource(nameof(Ordinal))]
        public void Equality_IsOrdinal(object value, object same, object otherCase)
        {
            Assert.AreEqual(value, same);
            Assert.AreEqual(value.GetHashCode(), same.GetHashCode());
            Assert.AreNotEqual(value, otherCase);
        }

        [Test]
        public void Operators_AgreeWithEquals()
        {
            Assert.IsTrue(new XsollaUserId("abc") == new XsollaUserId("abc"));
            Assert.IsTrue(new XsollaUserId("abc") != new XsollaUserId("ABC"));
            Assert.IsTrue(AvatarUrl.TryParse(Valid) == AvatarUrl.TryParse(Valid));
            Assert.IsTrue(AvatarUrl.TryParse(Valid) != AvatarUrl.None);
        }

        [Test]
        public void Newtypes_RejectNullAndEmpty()
        {
            // Unlike LongLeap.Core.Auth.IdToken, which silently returns None on null:
            // there it is easy to miss that the identity is missing.
            Assert.Throws<ArgumentException>(() => new AccessToken(null));
            Assert.Throws<ArgumentException>(() => new AccessToken(""));
            Assert.Throws<ArgumentException>(() => new RefreshToken(null));
            Assert.Throws<ArgumentException>(() => new XsollaUserId(""));
        }

        // ---- AvatarUrl.TryParse ----

        [Test]
        public void AvatarUrl_AcceptsAbsoluteHttps()
        {
            AvatarUrl url = AvatarUrl.TryParse(Valid);

            Assert.IsTrue(url.IsValid);
            Assert.AreEqual(Valid, url.Value);
        }

        [Test]
        public void AvatarUrl_NormalisesUppercaseScheme()
        {
            // Uri lowercases the scheme itself; check that the comparison with
            // UriSchemeHttps relies on that instead of breaking.
            Assert.IsTrue(AvatarUrl.TryParse("HTTPS://cdn.xsolla.com/a.png").IsValid);
        }

        [Test]
        public void AvatarUrl_TrimsSurroundingWhitespace()
        {
            AvatarUrl url = AvatarUrl.TryParse("  " + Valid + "  ");

            Assert.IsTrue(url.IsValid);
            Assert.AreEqual(Valid, url.Value, "the stored value must be ready to hand to a web request");
        }

        // Anything unusable becomes None and does NOT throw: picture is an
        // optional claim, and garbage in it must not break sign-in.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("http://cdn.xsolla.com/a.png")]
        [TestCase("data:image/png;base64,AAAA")]
        [TestCase("javascript:alert(1)")]
        [TestCase("/relative/a.png")]
        [TestCase("cdn.xsolla.com/a.png")]
        [TestCase("not a url at all")]
        public void AvatarUrl_RejectsAnythingButAbsoluteHttps(string raw)
        {
            Assert.IsFalse(AvatarUrl.TryParse(raw).IsValid, raw ?? "null");
        }
    }
}
