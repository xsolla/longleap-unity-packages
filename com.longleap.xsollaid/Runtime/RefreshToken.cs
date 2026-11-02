using System;

namespace LongLeap.XsollaId
{
    // Xsolla ID refresh token. Lives 720 hours (30 days) and is exchanged for a
    // new access token without the player's involvement — this is what lets the
    // game skip the sign-in window on every launch.
    //
    // More sensitive than the access token: it mints new tokens for its whole
    // lifetime. ToString() never prints the value.
    public readonly struct RefreshToken : IEquatable<RefreshToken>
    {
        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public RefreshToken(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException(
                    "RefreshToken: value cannot be empty — use None.", nameof(value));

            Value = value;
        }

        public static RefreshToken None => default;

        public bool Equals(RefreshToken other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RefreshToken other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => IsValid ? "RefreshToken(valid)" : "RefreshToken(none)";

        public static bool operator ==(RefreshToken left, RefreshToken right) => left.Equals(right);
        public static bool operator !=(RefreshToken left, RefreshToken right) => !left.Equals(right);
    }
}
