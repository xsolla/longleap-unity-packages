using System;

namespace LongLeap.XsollaId
{
    // Access token Xsolla ID (JWT). Предъявляется как Bearer при обращении к
    // userinfo и revoke.
    //
    // Это bearer-credential: кто держит строку — тот и есть пользователь, пока
    // токен не истёк. Поэтому ToString() значение НЕ печатает (как
    // LongLeap.Core.Auth.IdToken и как SanitizeUrl в FirebaseHttpClient):
    // токен в логе — это готовый вход в чужой аккаунт.
    public readonly struct AccessToken : IEquatable<AccessToken>
    {
        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public AccessToken(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException(
                    "AccessToken: value cannot be empty — use None.", nameof(value));

            Value = value;
        }

        public static AccessToken None => default;

        public bool Equals(AccessToken other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AccessToken other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => IsValid ? "AccessToken(valid)" : "AccessToken(none)";

        public static bool operator ==(AccessToken left, AccessToken right) => left.Equals(right);
        public static bool operator !=(AccessToken left, AccessToken right) => !left.Equals(right);
    }
}
