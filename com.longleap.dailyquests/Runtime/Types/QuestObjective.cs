namespace LongLeap.DailyQuests
{
    /// <summary>One measurable goal of a quest: a stat and how much of it to reach.</summary>
    public sealed class QuestObjective
    {
        public QuestObjectiveId Id { get; init; }

        public string StatPath { get; init; }

        public int StatTarget { get; init; }

        public QuestProgressMode Mode { get; init; }
    }
}
