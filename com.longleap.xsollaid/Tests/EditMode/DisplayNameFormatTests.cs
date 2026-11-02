using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class DisplayNameFormatTests
    {
        [TestCase(null, ExpectedResult = null)]
        [TestCase("", ExpectedResult = "")]
        [TestCase("   ", ExpectedResult = "")]
        [TestCase("user", ExpectedResult = "user")]
        [TestCase("editor user", ExpectedResult = "editor user")]
        [TestCase("user@mail.com", ExpectedResult = "user")]
        [TestCase("User@Mail.com", ExpectedResult = "User")]
        [TestCase(" user@mail.com ", ExpectedResult = "user")]
        [TestCase("user@mail@com", ExpectedResult = "user")]
        [TestCase("user@", ExpectedResult = "user")]
        // An empty local part is not an address, just garbage. Return it as is:
        // an odd name on screen is better than an empty one.
        [TestCase("@mail.com", ExpectedResult = "@mail.com")]
        [TestCase("@", ExpectedResult = "@")]
        public string LocalPart_StripsTheDomainOnly(string displayName) =>
            DisplayNameFormat.LocalPart(displayName);

        [Test]
        public void Session_ShortDisplayName_CutsTheDomainButKeepsTheRawClaim()
        {
            var session = new XsollaIdSession(
                new XsollaUserId("sub-1"), "user@mail.com",
                new AccessToken("access-1"), new RefreshToken("refresh-1"),
                expiresAtUnixMs: 1_700_000_000_000);

            Assert.AreEqual("user", session.ShortDisplayName);

            // Logs and the debug panel need the raw claim, so it must not be
            // replaced with the shortened one.
            Assert.AreEqual("user@mail.com", session.DisplayName);
        }

        [Test]
        public void Session_ShortDisplayName_SurvivesAMissingName()
        {
            var session = new XsollaIdSession(
                new XsollaUserId("sub-1"), null,
                new AccessToken("access-1"), new RefreshToken("refresh-1"),
                expiresAtUnixMs: 0);

            Assert.IsNull(session.ShortDisplayName);
        }
    }
}
