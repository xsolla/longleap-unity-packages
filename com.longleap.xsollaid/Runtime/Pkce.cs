using System;
using System.Security.Cryptography;
using System.Text;

namespace LongLeap.XsollaId
{
    // PKCE (RFC 7636) — то, что позволяет публичному клиенту обойтись без
    // секрета. Секрет в сборку WebGL класть нельзя: бандл читается открыто,
    // поэтому клиент регистрируется как public с обязательным PKCE.
    //
    // Смысл: перед входом генерим случайный verifier, отправляем на
    // /authorize только его SHA-256 (challenge), а verifier предъявляем уже
    // при обмене кода на токен. Перехвативший код без verifier'а бесполезен.
    public static class Pkce
    {
        // 32 байта → 43 символа base64url. RFC разрешает 43..128.
        private const int VerifierBytes = 32;
        private const int StateBytes = 16;

        // Источник случайности вынесен параметром: тесты подставляют
        // предсказуемые байты, а на WebGL при необходимости можно отдать
        // crypto.getRandomValues из jslib.
        //
        // ВНИМАНИЕ: работоспособность System.Security.Cryptography под
        // IL2CPP/WebGL надо подтвердить на реальной сборке. Если RNG там
        // недоступен — подменяем этот делегат, а НЕ UnityEngine.Random:
        // предсказуемый verifier сводит защиту PKCE к нулю.
        public static string CreateVerifier(Func<int, byte[]> randomBytes = null) =>
            Base64UrlEncode((randomBytes ?? CryptoRandomBytes)(VerifierBytes));

        public static string CreateState(Func<int, byte[]> randomBytes = null) =>
            Base64UrlEncode((randomBytes ?? CryptoRandomBytes)(StateBytes));

        // challenge = BASE64URL(SHA256(ASCII(verifier))), метод S256.
        public static string CreateChallengeS256(string verifier)
        {
            if (string.IsNullOrEmpty(verifier))
                throw new ArgumentException("Pkce: verifier cannot be empty.", nameof(verifier));

            using var sha = SHA256.Create();

            return Base64UrlEncode(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        // base64url без выравнивающих '='. Обычный Base64 в URL непригоден:
        // '+' и '/' там значат другое.
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
