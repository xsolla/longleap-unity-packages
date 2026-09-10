namespace LongLeap.DailyRewards
{
    public sealed class DailyRewardClaimResult
    {
        public DailyRewardDay Day { get; init; }
        public int Streak { get; init; }
        public int MaxStreak { get; init; }
        public bool IsCycleComplete { get; init; }
    }
}