using System;

namespace LongLeap.Core.Auth
{
    public readonly struct IdToken : IEquatable<IdToken>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public IdToken(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("IdToken: значение не может быть пустым.", nameof(value));
            Value = value;
        }

        public static IdToken None => default;

        public bool Equals(IdToken other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is IdToken other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => IsValid ? "IdToken(valid)" : "IdToken(none)";
        public static bool operator ==(IdToken left, IdToken right) => left.Equals(right);
        public static bool operator !=(IdToken left, IdToken right) => !left.Equals(right);
    }
}
