using System;

namespace LongLeap.Achievements
{
    public readonly struct GroupId : IEquatable<GroupId>
    {
        private readonly string _value;

        public GroupId(string value)
        {
            if (value != null && value.Length == 0)
                throw new ArgumentException("GroupId: value cannot be empty.", nameof(value));
            _value = value;
        }

        public string Value => _value;

        public static GroupId None => default;

        public bool IsNone => _value == null;

        public static implicit operator GroupId(string value) => new GroupId(value);

        public bool Equals(GroupId other) =>
            StringComparer.Ordinal.Equals(_value, other._value);

        public override bool Equals(object obj) =>
            obj is GroupId other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static bool operator ==(GroupId left, GroupId right) =>
            left.Equals(right);

        public static bool operator !=(GroupId left, GroupId right) =>
            !left.Equals(right);
    }
}
