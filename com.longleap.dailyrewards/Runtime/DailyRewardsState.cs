namespace LongLeap.DailyRewards
{
    public sealed class DailyRewardsState
    {
        public int Version;
        public int[] ClaimedDayIndices;
        public int Streak;
        public int MaxConsecutiveStreak;
        public long LastClaimUtcTicks;
        public int CurrentCycleNumber;
    }
}