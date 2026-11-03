namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Debug commands for quests — for dev tools and tests. Kept apart from
    /// <see cref="IDailyQuestService"/> so the game UI cannot accidentally complete
    /// a quest or change the set. <see cref="DailyQuestService"/> implements both.
    /// </summary>
    public interface IDailyQuestDebug
    {
        /// <summary>Mark a quest completed without touching stats.</summary>
        void ForceComplete(QuestId questId);

        /// <summary>Issue a new set right now, as if the period had changed.</summary>
        void SkipToNextPeriod();

        /// <summary>Wipe the state and issue the set again.</summary>
        void ResetState();
    }
}
