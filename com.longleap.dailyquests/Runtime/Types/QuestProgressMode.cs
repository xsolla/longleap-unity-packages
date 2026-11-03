namespace LongLeap.DailyQuests
{
    /// <summary>How a stat value turns into objective progress.</summary>
    public enum QuestProgressMode
    {
        /// <summary>
        /// How much the stat grew since the quest was issued: "grind 50 times"
        /// counts grinds in this period, not over a lifetime. For cumulative
        /// stats (Accumulate). The default.
        /// </summary>
        Delta = 0,

        /// <summary>
        /// The stat value itself. For stats the game resets on its own (the score
        /// of the current run), or when a threshold is needed rather than growth.
        /// </summary>
        Absolute = 1
    }
}
