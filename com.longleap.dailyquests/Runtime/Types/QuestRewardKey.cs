using System;

namespace LongLeap.DailyQuests
{
    public readonly struct QuestRewardKey : IEquatable<QuestRewardKey>
    {
        private readonly string _value;

        public QuestRewardKey(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("QuestRewardKey: значение не может быть пустым.", nameof(value));
            _value = value;
        }

        public string Value => _value;

        public static QuestRewardKey None => default;

        public bool IsNone => _value == null;

        public bool Equals(QuestRewardKey other) =>
            StringComparer.Ordinal.Equals(_value, other._value);

        public override bool Equals(object obj) =>
            obj is QuestRewardKey other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static bool operator ==(QuestRewardKey left, QuestRewardKey right) =>
            left.Equals(right);

        public static bool operator !=(QuestRewardKey left, QuestRewardKey right) =>
            !left.Equals(right);
    }
}
