namespace LongLeap.DailyRewards
{
    public sealed class DailyRewardDay
    {
        public int DayIndex { get; init; }
        public RewardEntry[] Rewards { get; init; }
    }
}