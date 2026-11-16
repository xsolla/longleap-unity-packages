using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class XsollaIdSessionTests
    {
        private static XsollaIdSession Session(string displayName = "Name", long expiresAtUnixMs = 1_000_000) =>
            new XsollaIdSession(
                new XsollaUserId("sub"), displayName,
                new AccessToken("access-secret"), new RefreshToken("refresh-secret"),
                expiresAtUnixMs);

        [Test]
        public void IsExpired_RespectsMargin()
        {
            XsollaIdSession session = Session();

            // 5-minute margin: 6 minutes before expiry it is still valid, 4 minutes before it is not.
            Assert.IsFalse(session.IsExpired(1_000_000 - 360_000, 300_000));
            Assert.IsTrue(session.IsExpired(1_000_000 - 240_000, 300_000));
        }

        [Test]
        public void WithoutAccessToken_IsAlwaysExpiredButRefreshable()
        {
            var session = new XsollaIdSession(
                new XsollaUserId("sub"), null, AccessToken.None, new RefreshToken("r"), long.MaxValue);

            Assert.IsTrue(session.IsExpired(0, 0));
            Assert.IsTrue(session.CanRefresh);
        }

        [Test]
        public void ToString_DoesNotLeakTokens()
        {
            string text = Session().ToString();

            StringAssert.DoesNotContain("access-secret", text);
            StringAssert.DoesNotContain("refresh-secret", text);
        }

        [Test]
        public void ShortDisplayName_CutsTheDomainButKeepsTheRawClaim()
        {
            XsollaIdSession session = Session("user@mail.com");

            Assert.AreEqual("user", session.ShortDisplayName);

            // Logs and the debug panel need the raw claim, so it must not be
            // replaced with the shortened one.
            Assert.AreEqual("user@mail.com", session.DisplayName);
        }

        [Test]
        public void ShortDisplayName_SurvivesAMissingName()
        {
            Assert.IsNull(Session(displayName: null).ShortDisplayName);
        }
    }
}
