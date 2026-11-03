namespace LongLeap.DailyQuests
{
    public enum DailyQuestStateLoadStatus
    {
        /// <summary>
        /// The store did not answer. The service turns off until restart: if it started
        /// from scratch, an already claimed quest would be issued again. This is the
        /// default value, so an unfilled result never turns quests on.
        /// </summary>
        Unavailable = 0,

        /// <summary>The save was read.</summary>
        Loaded = 1,

        /// <summary>There is no save yet — a fresh start.</summary>
        Empty = 2,

        /// <summary>
        /// A save exists but cannot be read (corrupted or from a newer version).
        /// What has already been claimed today is unknown, so no set is issued
        /// until the next reset.
        /// </summary>
        Unreadable = 3
    }

    /// <summary>What <see cref="IDailyQuestStateStore.LoadAsync"/> returned.</summary>
    public readonly struct DailyQuestStateLoadResult
    {
        public DailyQuestStateLoadStatus Status { get; }

        /// <summary>The loaded state; non-<c>null</c> only for <see cref="DailyQuestStateLoadStatus.Loaded"/>.</summary>
        public DailyQuestsState State { get; }

        private DailyQuestStateLoadResult(DailyQuestStateLoadStatus status, DailyQuestsState state)
        {
            Status = status;
            State = state;
        }

        public static DailyQuestStateLoadResult Loaded(DailyQuestsState state) =>
            state != null ? new DailyQuestStateLoadResult(DailyQuestStateLoadStatus.Loaded, state) : Empty();

        public static DailyQuestStateLoadResult Empty() => new(DailyQuestStateLoadStatus.Empty, null);

        public static DailyQuestStateLoadResult Unreadable() => new(DailyQuestStateLoadStatus.Unreadable, null);

        public static DailyQuestStateLoadResult Unavailable() => new(DailyQuestStateLoadStatus.Unavailable, null);

        public override string ToString() => Status.ToString();
    }
}
