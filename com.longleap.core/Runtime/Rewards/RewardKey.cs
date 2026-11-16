using System;

namespace LongLeap.Core
{
    /// <summary>
    /// Reward key: what to grant. Packages (daily rewards, quests) do not interpret
    /// the key — the game decides what "soft" is (a currency, an item, a booster).
    /// </summary>
    public readonly struct RewardKey : IEquatable<RewardKey>
    {
        private readonly string _value;

        public RewardKey(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("RewardKey: value cannot be empty.", nameof(value));
            _value = value;
        }

        public string Value => _value;

        public static RewardKey None => default;

        public bool IsNone => _value == null;

        public bool Equals(RewardKey other) =>
            StringComparer.Ordinal.Equals(_value, other._value);

        public override bool Equals(object obj) =>
            obj is RewardKey other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static bool operator ==(RewardKey left, RewardKey right) =>
            left.Equals(right);

        public static bool operator !=(RewardKey left, RewardKey right) =>
            !left.Equals(right);
    }
}