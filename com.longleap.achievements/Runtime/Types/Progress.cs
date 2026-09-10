using System;

namespace LongLeap.Achievements
{
    public readonly struct Progress : IEquatable<Progress>
    {
        public int Current { get; }
        public int Target { get; }

        public Progress(int current, int target)
        {
            Current = current;
            Target = target;
        }

        public float Ratio => Target > 0 ? (float)Current / Target : 0f;

        public bool IsComplete => Current >= Target;

        public bool Equals(Progress other) =>
            Current == other.Current && Target == other.Target;

        public override bool Equals(object obj) =>
            obj is Progress other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Current, Target);

        public override string ToString() =>
            $"Progress({Current}, {Target})";

        public static bool operator ==(Progress left, Progress right) =>
            left.Equals(right);

        public static bool operator !=(Progress left, Progress right) =>
            !left.Equals(right);
    }
}
