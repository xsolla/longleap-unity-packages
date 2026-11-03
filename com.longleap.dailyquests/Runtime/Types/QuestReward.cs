namespace LongLeap.DailyQuests
{
    /// <summary>
    /// What the player gets for claiming the quest. The package does not interpret the key:
    /// what "soft" is (currency, item, experience) is decided by the game in
    /// <see cref="IDailyQuestGameBridge.ApplyRewards"/>.
    /// </summary>
    public sealed class QuestReward
    {
        public QuestRewardKey Key { get; init; }

        public int Amount { get; init; }
    }
}
