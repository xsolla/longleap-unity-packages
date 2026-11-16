namespace LongLeap.DailyQuests
{
    public enum QuestState
    {
        /// <summary>Objectives are not met yet.</summary>
        InProgress = 0,

        /// <summary>Objectives are met, the reward is waiting for the player to claim it.</summary>
        Completed = 1,

        /// <summary>The reward has been claimed.</summary>
        Claimed = 2
    }
}
