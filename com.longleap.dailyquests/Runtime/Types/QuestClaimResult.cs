namespace LongLeap.DailyQuests
{
    public sealed class QuestClaimResult
    {
        public QuestDefinition Quest { get; init; }
        public QuestReward[] Rewards { get; init; }
    }
}
