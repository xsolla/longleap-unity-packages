using System;

namespace LongLeap.DailyRewards
{
    public readonly struct RewardId : IEquatable<RewardId>
    {
        private readonly string _value;

        public RewardId(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("RewardId: значение не может быть пустым.", nameof(value));
            _value = value;
        }

        public string Value => _value;

        public static RewardId None => default;

        public bool IsNone => _value == null;

        public bool Equals(RewardId other) =>
            StringComparer.Ordinal.Equals(_value, other._value);

        public override bool Equals(object obj) =>
            obj is RewardId other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static bool operator ==(RewardId left, RewardId right) =>
            left.Equals(right);

        public static bool operator !=(RewardId left, RewardId right) =>
            !left.Equals(right);
    }
}