using System;
using UnityEngine;

namespace LongLeap.Localization
{
    [Serializable]
    public struct LocKey : IEquatable<LocKey>
    {
        [SerializeField] private string Value;

        public LocKey(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("LocKey: значение не может быть пустым.", nameof(value));
            Value = value;
        }

        public static LocKey Format(string format, params object[] args)
            => new(string.Format(format, args));

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public static implicit operator string(LocKey key) => key.Value;
        public static implicit operator LocKey(string value) => new(value);

        public override string ToString() => Value ?? string.Empty;

        public bool Equals(LocKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is LocKey other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;

        public static bool operator ==(LocKey left, LocKey right) => left.Equals(right);
        public static bool operator !=(LocKey left, LocKey right) => !left.Equals(right);
    }
}
