using System;

namespace LongLeap.Achievements
{
    public readonly struct AchievementId : IEquatable<AchievementId>
    {
        private readonly string _value;

        public AchievementId(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("AchievementId: значение не может быть пустым.", nameof(value));
            _value = value;
        }

        public string Value => _value;

        public static AchievementId None => default;

        public bool IsNone => _value == null;

        public bool Equals(AchievementId other) =>
            StringComparer.Ordinal.Equals(_value, other._value);

        public override bool Equals(object obj) =>
            obj is AchievementId other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static bool operator ==(AchievementId left, AchievementId right) =>
            left.Equals(right);

        public static bool operator !=(AchievementId left, AchievementId right) =>
            !left.Equals(right);
    }
}
