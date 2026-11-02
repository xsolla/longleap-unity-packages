using System;

namespace LongLeap.XsollaId
{
    // Xsolla ID access token (JWT). Sent as a Bearer token to userinfo and
    // revoke.
    //
    // This is a bearer credential: whoever holds the string is the user until
    // the token expires. That is why ToString() never prints the value (same as
    // LongLeap.Core.Auth.IdToken and SanitizeUrl in FirebaseHttpClient): a
    // token in a log is a ready-made way into someone else's account.
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
