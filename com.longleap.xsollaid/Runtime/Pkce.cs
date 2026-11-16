using System;
using System.Security.Cryptography;
using System.Text;

namespace LongLeap.XsollaId
{
    // PKCE (RFC 7636) — what lets a public client work without a secret. A
    // secret cannot be shipped in a WebGL build: the bundle is readable by
    // anyone, so the client is registered as public with PKCE required.
    //
    // How it works: before sign-in a random verifier is generated, only its
    // SHA-256 (the challenge) is sent to /authorize, and the verifier itself is
    // presented when exchanging the code for tokens. An intercepted code is
    // useless without the verifier.
    public static class Pkce
    {
        // 32 bytes → 43 base64url characters. The RFC allows 43..128.
        private const int VerifierBytes = 32;
        private const int StateBytes = 16;

        // The randomness source is a parameter: tests pass predictable bytes,
        // and on WebGL crypto.getRandomValues from a jslib can be supplied if
        // needed.
        //
        // NOTE: System.Security.Cryptography under IL2CPP/WebGL should be
        // verified on a real build. If the RNG is unavailable there, replace
        // this delegate — NOT with UnityEngine.Random: a predictable verifier
        // reduces PKCE protection to nothing.
        public static string CreateVerifier(Func<int, byte[]> randomBytes = null) =>
            Base64UrlEncode((randomBytes ?? CryptoRandomBytes)(VerifierBytes));

        public static string CreateState(Func<int, byte[]> randomBytes = null) =>
            Base64UrlEncode((randomBytes ?? CryptoRandomBytes)(StateBytes));

        // challenge = BASE64URL(SHA256(ASCII(verifier))), method S256.
        public static string CreateChallengeS256(string verifier)
        {
            if (string.IsNullOrEmpty(verifier))
                throw new ArgumentException("Pkce: verifier cannot be empty.", nameof(verifier));

            using var sha = SHA256.Create();

            return Base64UrlEncode(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        // base64url without '=' padding. Plain Base64 is unusable in a URL:
        // '+' and '/' mean something else there.
        public static string Base64UrlEncode(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] CryptoRandomBytes(int count)
        {
            var buffer = new byte[count];

            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(buffer);

            return buffer;
        }
    }
}
