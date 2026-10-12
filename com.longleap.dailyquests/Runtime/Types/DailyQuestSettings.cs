namespace LongLeap.DailyQuests
{
    public sealed class DailyQuestSettings
    {
        public const int DefaultDailyCount = 3;

        /// <summary>Сколько квестов выдаётся на один период.</summary>
        public int DailyCount { get; init; } = DefaultDailyCount;

        /// <summary>Час по UTC, в который набор квестов меняется на новый.</summary>
        public int ResetHourUtc { get; init; }

        public bool UniqueCategories { get; init; }
        public bool HardestLast { get; init; }
        public int[] SlotDifficulties { get; init; } = System.Array.Empty<int>();
    }
}
