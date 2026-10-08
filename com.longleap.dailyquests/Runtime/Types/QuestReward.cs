namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Что получит игрок, забрав квест. Ключ пакет не толкует: что такое
    /// «soft» (валюта, предмет, опыт), решает игра в
    /// <see cref="IDailyQuestGameBridge.ApplyRewards"/>.
    /// </summary>
    public sealed class QuestReward
    {
        public QuestRewardKey Key { get; init; }

        public int Amount { get; init; }
    }
}
