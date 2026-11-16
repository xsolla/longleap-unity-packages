namespace LongLeap.DailyQuests
{
    public sealed class DailyQuestSettings
    {
        public const int DefaultDailyCount = 3;

        /// <summary>How many quests are issued per period.</summary>
        public int DailyCount { get; init; } = DefaultDailyCount;

        /// <summary>Hour (UTC) at which the quest set is replaced with a new one.</summary>
        public int ResetHourUtc { get; init; }

        /// <summary>No two quests in the set share a category (<see cref="QuestDefinition.Categories"/>).</summary>
        public bool UniqueCategories { get; init; }

        /// <summary>
        /// The set goes from easy to hard. When <see cref="SlotDifficulties"/> are not
        /// set — the set has exactly one quest of the pool's highest difficulty.
        /// </summary>
        public bool HardestLast { get; init; }

        /// <summary>
        /// Difficulty of each place in the set: <c>[1, 2, 3]</c> — easy, medium,
        /// hard. Shorter than <see cref="DailyCount"/> — the remaining places take any
        /// difficulty; longer — the hardest ones are used.
        /// </summary>
        public int[] SlotDifficulties { get; init; } = System.Array.Empty<int>();

        /// <summary>The set is picked by rules — see <see cref="DailyQuestSelector"/>.</summary>
        public bool HasSelectionRules =>
            UniqueCategories || HardestLast || (SlotDifficulties?.Length ?? 0) > 0;
    }
}
