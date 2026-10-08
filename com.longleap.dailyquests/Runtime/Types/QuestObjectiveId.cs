using System;

namespace LongLeap.DailyQuests
{
    public readonly struct QuestObjectiveId : IEquatable<QuestObjectiveId>
    {
        private readonly string _value;

        public QuestObjectiveId(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("QuestObjectiveId: значение не может быть пустым.", nameof(value));
            _value = value;
        }

        public string Value => _value;

        public static QuestObjectiveId None => default;

        public bool IsNone => _value == null;

        public bool Equals(QuestObjectiveId other) =>
            StringComparer.Ordinal.Equals(_value, other._value);

        public override bool Equals(object obj) =>
            obj is QuestObjectiveId other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static bool operator ==(QuestObjectiveId left, QuestObjectiveId right) =>
            left.Equals(right);

        public static bool operator !=(QuestObjectiveId left, QuestObjectiveId right) =>
            !left.Equals(right);
    }
}
