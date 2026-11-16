using System;

namespace LongLeap.Core
{
    /// <summary>
    /// Daily reset schedule (daily quests, daily rewards): a period lasts a day and starts at
    /// <see cref="ResetHourUtc"/>. Hours outside 0..23 wrap around (25 → 1, −1 → 23).
    /// </summary>
    public sealed class ResetSchedule
    {
        public int ResetHourUtc { get; }

        public ResetSchedule(int resetHourUtc)
        {
            ResetHourUtc = ((resetHourUtc % 24) + 24) % 24;
        }

        /// <summary>Start of the period that contains <paramref name="utcNow"/>.</summary>
        public DateTime PeriodStart(DateTime utcNow)
        {
            var reset = utcNow.Date.AddHours(ResetHourUtc);
            return utcNow < reset ? reset.AddDays(-1) : reset;
        }

        public DateTime NextReset(DateTime utcNow) => PeriodStart(utcNow).AddDays(1);

        public TimeSpan TimeUntilReset(DateTime utcNow) => NextReset(utcNow) - utcNow;
    }
}
