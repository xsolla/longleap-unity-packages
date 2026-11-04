namespace LongLeap.DailyRewards
{
    public sealed class DailyRewardsState
    {
        /// <summary>
        /// Save format version. 2 — <see cref="ClaimedCount"/> instead of the
        /// ClaimedDayIndices array (v1 is migrated in <see cref="JsonDailyRewardsStateStore.Parse"/>).
        /// A newer save is not read.
        /// </summary>
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;

        /// <summary>
        /// How many days of the current cycle are claimed. Days are claimed strictly in order,
        /// so these are days 0..ClaimedCount-1.
        /// </summary>
        public int ClaimedCount;

        public int Streak;
        public int MaxConsecutiveStreak;
        public long LastClaimUtcTicks;
        public int CurrentCycleNumber;

        /// <summary>A clean state — first launch.</summary>
        public static DailyRewardsState Empty() => new DailyRewardsState();

        internal DailyRewardsState Clone() => (DailyRewardsState)MemberwiseClone();
    }
}
