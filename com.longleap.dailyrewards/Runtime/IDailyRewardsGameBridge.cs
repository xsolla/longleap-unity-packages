namespace LongLeap.DailyRewards
{
    public interface IDailyRewardsGameBridge
    {
        void ApplyReward(DailyRewardClaimResult result);
        void OnStreakChanged(int streak, int maxStreak);
        void OnCycleCompleted(int cycleNumber);
        void OnStreakReset();
    }
}