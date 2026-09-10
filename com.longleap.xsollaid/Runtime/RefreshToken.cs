using System;

namespace LongLeap.XsollaId
{
    // Refresh token Xsolla ID. Живёт 720 часов (30 суток) и обменивается на
    // новый access token без участия игрока — именно он позволяет не
    // показывать окно входа при каждом запуске.
    //
    // Секретнее access token'а: по нему выпускают новые токены весь срок
    // жизни. ToString() значение не печатает никогда.
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
