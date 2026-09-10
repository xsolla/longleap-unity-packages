using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class DisplayNameFormatTests
    {
        [TestCase(null, ExpectedResult = null)]
        [TestCase("", ExpectedResult = "")]
        [TestCase("   ", ExpectedResult = "")]
        [TestCase("anton", ExpectedResult = "anton")]
        [TestCase("anton smirnov", ExpectedResult = "anton smirnov")]
        [TestCase("anton@mail.com", ExpectedResult = "anton")]
        [TestCase("Anton@Mail.com", ExpectedResult = "Anton")]
        [TestCase(" anton@mail.com ", ExpectedResult = "anton")]
        [TestCase("anton@mail@com", ExpectedResult = "anton")]
        [TestCase("anton@", ExpectedResult = "anton")]
        [TestCase("Антон@почта.рф", ExpectedResult = "Антон")]
        // Пустая локальная часть — это не адрес, а мусор. Отдаём как есть:
        // странное имя на экране лучше пустого места.
        [TestCase("@mail.com", ExpectedResult = "@mail.com")]
        [TestCase("@", ExpectedResult = "@")]
        public string LocalPart_StripsTheDomainOnly(string displayName) =>
            DisplayNameFormat.LocalPart(displayName);

        [Test]
        public void Session_ShortDisplayName_CutsTheDomainButKeepsTheRawClaim()
        {
            var session = new XsollaIdSession(
                new XsollaUserId("sub-1"), "anton@mail.com",
                new AccessToken("access-1"), new RefreshToken("refresh-1"),
                expiresAtUnixMs: 1_700_000_000_000);

            Assert.AreEqual("anton", session.ShortDisplayName);

            // Сырой клейм нужен логам и отладочной панели — подменять его
            // укороченным нельзя.
            Assert.AreEqual("anton@mail.com", session.DisplayName);
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
