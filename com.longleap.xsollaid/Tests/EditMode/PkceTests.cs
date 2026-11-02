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
        public void Base64UrlEncode_HasNoPaddingAndNoUrlUnsafeChars()
        {
            // 10 bytes give base64 with two trailing '=' - a good edge case.
            string encoded = Pkce.Base64UrlEncode(new byte[] { 251, 255, 190, 1, 2, 3, 4, 5, 6, 7 });

            Assert.IsFalse(encoded.Contains("="), "padding must be stripped");
            Assert.IsFalse(encoded.Contains("+"), "'+' is not allowed in a URL");
            Assert.IsFalse(encoded.Contains("/"), "'/' is not allowed in a URL");
        }

        [Test]
        public void Base64UrlEncode_ReplacesPlusAndSlashWithDashAndUnderscore()
        {
            // 0xFB 0xFF 0xBE encode to "+/++" in plain base64 - checks the substitution.
            string encoded = Pkce.Base64UrlEncode(new byte[] { 0xFB, 0xFF, 0xBE });

            Assert.AreEqual(Convert.ToBase64String(new byte[] { 0xFB, 0xFF, 0xBE })
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'), encoded);
        }

        [Test]
        public void CreateVerifier_Is43CharsAndUrlSafe()
        {
            string verifier = Pkce.CreateVerifier();

            // 32 bytes in unpadded base64url are exactly 43 characters, within
            // the 43..128 range allowed by the RFC.
            Assert.AreEqual(43, verifier.Length);
            Assert.IsFalse(verifier.Contains("="));
            Assert.IsFalse(verifier.Contains("+"));
            Assert.IsFalse(verifier.Contains("/"));
        }

        [Test]
        public void CreateVerifier_UsesInjectedRandomSource()
        {
            byte[] fixedBytes = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");

            string first = Pkce.CreateVerifier(_ => fixedBytes);
            string second = Pkce.CreateVerifier(_ => fixedBytes);

            Assert.AreEqual(first, second, "the same bytes must produce the same verifier");
            Assert.AreEqual(Pkce.Base64UrlEncode(fixedBytes), first);
        }

        [Test]
        public void CreateVerifier_TwoCallsDiffer()
        {
            // A predictable verifier defeats the purpose of PKCE.
            Assert.AreNotEqual(Pkce.CreateVerifier(), Pkce.CreateVerifier());
        }

        [Test]
        public void CreateChallengeS256_RejectsEmptyVerifier()
        {
            Assert.Throws<ArgumentException>(() => Pkce.CreateChallengeS256(""));
            Assert.Throws<ArgumentException>(() => Pkce.CreateChallengeS256(null));
        }
    }
}
