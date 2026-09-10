namespace LongLeap.DailyRewards
{
    public sealed class RewardEntry
    {
        public RewardKey Key { get; init; }
        public RewardId RewardId { get; init; }
        public int Amount { get; init; }
    }
}