using System;

namespace LongLeap.DailyQuests
{
    public readonly struct QuestId : IEquatable<QuestId>
    {
        private readonly string _value;

        public QuestId(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("QuestId: значение не может быть пустым.", nameof(value));
            _value = value;
        }

        public string Value => _value;

        public static QuestId None => default;

        public bool IsNone => _value == null;

        public bool Equals(QuestId other) =>
            StringComparer.Ordinal.Equals(_value, other._value);

        public override bool Equals(object obj) =>
            obj is QuestId other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static bool operator ==(QuestId left, QuestId right) =>
            left.Equals(right);

        public static bool operator !=(QuestId left, QuestId right) =>
            !left.Equals(right);
    }
}
