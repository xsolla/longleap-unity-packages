namespace LongLeap.DailyQuests
{
    /// <summary>A quest from the pool — what lives in the JSON.</summary>
    public sealed class QuestDefinition
    {
        public QuestId Id { get; init; }

        /// <summary>
        /// Weight when picking the set for a period: a quest with weight 2 comes up twice
        /// as often as one with weight 1. 0 — the quest stays in the pool but is never picked.
        /// </summary>
        public int Weight { get; init; } = 1;

        /// <summary>
        /// Categories for <see cref="DailyQuestSettings.UniqueCategories"/>. A quest without
        /// categories never conflicts.
        /// </summary>
        public string[] Categories { get; init; } = System.Array.Empty<string>();

        /// <summary>Difficulty for <see cref="DailyQuestSettings.SlotDifficulties"/> and <see cref="DailyQuestSettings.HardestLast"/>.</summary>
        public int Difficulty { get; init; }

        public QuestObjective[] Objectives { get; init; }

        public QuestReward[] Rewards { get; init; }
    }
}
