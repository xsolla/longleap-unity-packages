namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Where quests meet the game. The package grants nothing by itself —
    /// the game puts the reward into the wallet in <see cref="ApplyRewards"/>. That is why the bridge
    /// is required: without it a quest would be marked claimed and the reward lost.
    /// </summary>
    public interface IDailyQuestGameBridge
    {
        /// <summary>A new set was issued (a new period has started).</summary>
        void OnQuestsRotated(QuestStatus[] active);

        /// <summary>A quest is complete — the reward can be claimed. It has not been granted yet.</summary>
        void OnQuestCompleted(QuestStatus status);

        /// <summary>The player claimed the reward — grant it.</summary>
        void ApplyRewards(QuestClaimResult result);
    }
}
