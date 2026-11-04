namespace LongLeap.DailyRewards
{
    /// <summary>
    /// Link to the game with a single duty: grant the reward of a claimed day. What happened
    /// to the streak and the cycle is in <see cref="DailyRewardClaimResult"/>; the UI has
    /// <see cref="IDailyRewardsService.Changed"/>.
    /// </summary>
    public interface IDailyRewardsGameBridge
    {
        /// <summary>
        /// Grant the reward. Called after the claimed day is saved. An exception thrown
        /// here is logged and does not roll the claim back: the day is already saved.
        /// </summary>
        void ApplyReward(DailyRewardClaimResult result);
    }
}
