using System.Collections.Generic;

namespace LongLeap.DailyRewards
{
    public sealed class DailyRewardDay
    {
        /// <summary>Zero-based day index — the position in the calendar.</summary>
        public int DayIndex { get; init; }

        public IReadOnlyList<RewardEntry> Rewards { get; init; }
    }
}
