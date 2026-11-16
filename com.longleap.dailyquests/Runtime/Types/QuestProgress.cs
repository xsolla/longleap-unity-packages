using System;

namespace LongLeap.DailyQuests
{
    public readonly struct QuestProgress : IEquatable<QuestProgress>
    {
        public int Current { get; }
        public int Target { get; }

        public QuestProgress(int current, int target)
        {
            Current = current;
            Target = target;
        }

        public float Ratio => Target > 0 ? Math.Min(1f, (float)Current / Target) : 0f;

        public bool IsComplete => Current >= Target;

        public bool Equals(QuestProgress other) =>
            Current == other.Current && Target == other.Target;

        public override bool Equals(object obj) =>
            obj is QuestProgress other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Current, Target);

        public override string ToString() =>
            $"QuestProgress({Current}, {Target})";

        public static bool operator ==(QuestProgress left, QuestProgress right) =>
            left.Equals(right);

        public static bool operator !=(QuestProgress left, QuestProgress right) =>
            !left.Equals(right);
    }
}
