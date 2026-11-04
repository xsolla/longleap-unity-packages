namespace LongLeap.DailyRewards
{
    public enum DailyRewardsStateLoadStatus
    {
        /// <summary>
        /// The store did not answer. The service turns off until restart: if it started from
        /// scratch, an already claimed day would be granted again. This is the default value,
        /// so an unfilled result never turns rewards on.
        /// </summary>
        Unavailable = 0,

        /// <summary>The save was read.</summary>
        Loaded = 1,

        /// <summary>There is no save yet — first launch.</summary>
        Empty = 2,

        /// <summary>
        /// A save exists but cannot be read (corrupted or from a newer
        /// version). Whether today's reward was claimed is unknown, so the calendar
        /// starts over from the next reset.
        /// </summary>
        Unreadable = 3
    }

    /// <summary>What <see cref="IDailyRewardsStateStore.LoadAsync"/> returned.</summary>
    public readonly struct DailyRewardsStateLoadResult
    {
        public DailyRewardsStateLoadStatus Status { get; }

        /// <summary>The loaded state; non-<c>null</c> only for <see cref="DailyRewardsStateLoadStatus.Loaded"/>.</summary>
        public DailyRewardsState State { get; }

        private DailyRewardsStateLoadResult(DailyRewardsStateLoadStatus status, DailyRewardsState state)
        {
            Status = status;
            State = state;
        }

        public static DailyRewardsStateLoadResult Loaded(DailyRewardsState state) =>
            state != null ? new DailyRewardsStateLoadResult(DailyRewardsStateLoadStatus.Loaded, state) : Empty();

        public static DailyRewardsStateLoadResult Empty() => new(DailyRewardsStateLoadStatus.Empty, null);

        public static DailyRewardsStateLoadResult Unreadable() => new(DailyRewardsStateLoadStatus.Unreadable, null);

        public static DailyRewardsStateLoadResult Unavailable() => new(DailyRewardsStateLoadStatus.Unavailable, null);

        public override string ToString() => Status.ToString();
    }
}
