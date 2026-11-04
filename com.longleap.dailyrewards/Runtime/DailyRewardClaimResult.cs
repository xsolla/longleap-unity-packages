namespace LongLeap.DailyRewards
{
    public sealed class DailyRewardClaimResult
    {
        /// <summary>The claimed day and its rewards.</summary>
        public DailyRewardDay Day { get; init; }

        /// <summary>Streak after this claim.</summary>
        public int Streak { get; init; }

        public int MaxStreak { get; init; }

        /// <summary>The streak was reset before this claim (a day was missed) — <see cref="Streak"/> started over.</summary>
        public bool StreakWasReset { get; init; }

        /// <summary>This claim took the last day of the cycle.</summary>
        public bool CompletedCycle { get; init; }

        /// <summary>Total number of completed cycles, including this one.</summary>
        public int CycleNumber { get; init; }

        /// <summary>The calendar is finished for good (<see cref="DailyRewardCyclePolicy.Once"/>) — no more rewards.</summary>
        public bool IsCycleComplete { get; init; }
    }
}
