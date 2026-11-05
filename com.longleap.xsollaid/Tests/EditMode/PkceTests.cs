using System;
using System.Text;
using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class PkceTests
    {
        // Reference example from RFC 7636, Appendix B. If it diverges, the server
        // rejects the code exchange, and finding out why is very hard.
        private const string RfcVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        private const string RfcChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        [Test]
        public void CreateChallengeS256_MatchesRfc7636Vector()
        {
            Assert.AreEqual(RfcChallenge, Pkce.CreateChallengeS256(RfcVerifier));
        }

        [Test]
        public void Base64UrlEncode_UsesTheUrlAlphabetWithoutPadding()
        {
            // 0xFB 0xFF 0xBE are "+/++" in plain base64, and 10 bytes end with
            // "==" — both substitutions and the padding are exercised.
            // Plain base64: "+/++AQIDBAUGBw==".
            byte[] bytes = { 0xFB, 0xFF, 0xBE, 1, 2, 3, 4, 5, 6, 7 };

            Assert.AreEqual("-_--AQIDBAUGBw", Pkce.Base64UrlEncode(bytes));
        }

        [Test]
        public void CreateVerifier_Is43UrlSafeChars()
        {
            // 32 bytes in unpadded base64url are exactly 43 characters, within the
            // 43..128 range allowed by the RFC.
            StringAssert.IsMatch("^[A-Za-z0-9_-]{43}$", Pkce.CreateVerifier());
        }

        [Test]
        public void CreateVerifier_UsesInjectedRandomSource()
        {
            byte[] fixedBytes = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");

            Assert.AreEqual(Pkce.Base64UrlEncode(fixedBytes), Pkce.CreateVerifier(_ => fixedBytes));
        }

        [Test]
        public void CreateVerifier_TwoCallsDiffer()
        {
            // A predictable verifier defeats the purpose of PKCE.
            Assert.AreNotEqual(Pkce.CreateVerifier(), Pkce.CreateVerifier());
        }

        // state is the CSRF check of the callback: it must be URL-safe and
        // unpredictable. 16 bytes are 22 unpadded base64url characters.
        [Test]
        public void CreateState_IsUrlSafeAndDiffersEachTime()
        {
            string first = Pkce.CreateState();

            StringAssert.IsMatch("^[A-Za-z0-9_-]{22}$", first);
            Assert.AreNotEqual(first, Pkce.CreateState());
        }

        [Test]
        public void CreateChallengeS256_RejectsEmptyVerifier()
        {
            Assert.Throws<ArgumentException>(() => Pkce.CreateChallengeS256(""));
            Assert.Throws<ArgumentException>(() => Pkce.CreateChallengeS256(null));
        }
    }
}
