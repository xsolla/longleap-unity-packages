using System;
using System.Threading.Tasks;
using LongLeap.Core.Auth;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaAccountAuthProviderTests
    {
        [Test]
        public async Task Login_ReturnsTheAccountAsUid()
        {
            var auth = new XsollaAccountAuthProvider("account-1");

            var result = await auth.LoginAsync() as AuthResult.Success;

            Assert.IsNotNull(result);
            Assert.AreEqual("account-1", result.Uid.Value);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Constructor_RejectsEmptyAccount(string accountId)
        {
            Assert.Throws<ArgumentException>(() => new XsollaAccountAuthProvider(accountId));
        }
    }
}
