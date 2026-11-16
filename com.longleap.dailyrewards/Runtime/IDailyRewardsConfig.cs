using System;
using System.Collections.Generic;

namespace LongLeap.DailyRewards
{
    public interface IDailyRewardsConfig
    {
        /// <summary>
        /// Calendar days in order. The number of days is the list length, a day index is its
        /// position (the service sets <see cref="DailyRewardDay.DayIndex"/> itself).
        /// An empty list disables daily rewards.
        /// </summary>
        IReadOnlyList<DailyRewardDay> Days { get; }

        DailyRewardCyclePolicy CyclePolicy { get; }
        int ResetHourUtc { get; }
        TimeSpan GracePeriod { get; }
    }
}
