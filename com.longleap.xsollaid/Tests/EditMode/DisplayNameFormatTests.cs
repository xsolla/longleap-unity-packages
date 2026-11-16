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
    }
}
