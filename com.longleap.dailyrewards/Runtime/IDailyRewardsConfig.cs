using System;

namespace LongLeap.DailyRewards
{
    public interface IDailyRewardsConfig
    {
        int DayCount { get; }
        DailyRewardDay[] Days { get; }
        DailyRewardCyclePolicy CyclePolicy { get; }
        int ResetHourUtc { get; }
        TimeSpan GracePeriod { get; }
    }
}