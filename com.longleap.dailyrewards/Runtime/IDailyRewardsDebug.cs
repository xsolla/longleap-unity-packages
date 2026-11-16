namespace LongLeap.DailyRewards
{
    /// <summary>
    /// Debug commands for the calendar — for dev tools and tests. Kept apart from
    /// <see cref="IDailyRewardsService"/> so the game UI cannot accidentally wipe
    /// progress or skip time. <see cref="DailyRewardsService"/> implements both.
    /// </summary>
    public interface IDailyRewardsDebug
    {
        /// <summary>Make today's reward claimable, as if the next reset had happened.</summary>
        void SkipToNextResetWindow();

        /// <summary>Wipe progress: claimed days, streaks, cycle number.</summary>
        void ResetState();
    }
}
