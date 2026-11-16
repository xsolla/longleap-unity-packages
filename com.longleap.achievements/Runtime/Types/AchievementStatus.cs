using System;

namespace LongLeap.Achievements
{
    public sealed class AchievementStatus
    {
        public AchievementDefinition Definition { get; init; }
        public bool IsAvailable { get; init; }
        public bool IsUnlocked { get; init; }

        /// <summary>
        /// When the achievement was unlocked. Null if it is locked, or if the
        /// store did not keep the time.
        /// </summary>
        public DateTimeOffset? UnlockedAt { get; init; }

        public ObjectiveProgress[] ObjectiveProgresses { get; init; }
    }
}
