using System;

namespace LongLeap.Core.Auth
{
    public readonly struct UserId : IEquatable<UserId>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public UserId(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("UserId: значение не может быть пустым.", nameof(value));
            Value = value;
        }

        public static UserId None => default;

        public bool Equals(UserId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is UserId other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => IsValid ? "UserId(valid)" : "UserId(none)";
        public static bool operator ==(UserId left, UserId right) => left.Equals(right);
        public static bool operator !=(UserId left, UserId right) => !left.Equals(right);
    }
}
