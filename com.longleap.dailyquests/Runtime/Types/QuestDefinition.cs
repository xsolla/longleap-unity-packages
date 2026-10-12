namespace LongLeap.DailyQuests
{
    /// <summary>Квест из пула — то, что лежит в JSON.</summary>
    public sealed class QuestDefinition
    {
        public QuestId Id { get; init; }

        /// <summary>
        /// Вес при выборе набора на период: квест с весом 2 выпадает вдвое чаще
        /// квеста с весом 1. 0 — квест остаётся в пуле, но не выпадает.
        /// </summary>
        public int Weight { get; init; } = 1;

        public string[] Categories { get; init; } = System.Array.Empty<string>();
        public int Difficulty { get; init; }

        public QuestObjective[] Objectives { get; init; }

        public QuestReward[] Rewards { get; init; }
    }
}
