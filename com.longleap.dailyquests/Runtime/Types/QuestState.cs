namespace LongLeap.DailyQuests
{
    public enum QuestState
    {
        /// <summary>Цели ещё не набраны.</summary>
        InProgress = 0,

        /// <summary>Цели набраны, награда ждёт, пока игрок её заберёт.</summary>
        Completed = 1,

        /// <summary>Награда забрана.</summary>
        Claimed = 2
    }
}
