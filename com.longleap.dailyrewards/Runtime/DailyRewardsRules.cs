using System;
using LongLeap.Core;

namespace LongLeap.DailyRewards
{
    /// <summary>What happened on a claim — for <see cref="DailyRewardClaimResult"/>.</summary>
    internal readonly struct ClaimOutcome
    {
        public int DayIndex { get; }
        public bool StreakWasReset { get; }
        public bool CompletedCycle { get; }

        public ClaimOutcome(int dayIndex, bool streakWasReset, bool completedCycle)
        {
            DayIndex = dayIndex;
            StreakWasReset = streakWasReset;
            CompletedCycle = completedCycle;
        }
    }

    /// <summary>
    /// Calendar rules without storage, events or a clock: what can be claimed now,
    /// what the state becomes after a claim, what state a day is in. Time is passed
    /// in as a parameter; the given state is never changed — a claim returns a new one.
    /// </summary>
    internal sealed class DailyRewardsRules
    {
        private readonly DailyRewardCyclePolicy _policy;
        private readonly TimeSpan _gracePeriod;

        public DailyRewardsRules(int dayCount, DailyRewardCyclePolicy policy, TimeSpan gracePeriod, ResetSchedule schedule)
        {
            DayCount = Math.Max(0, dayCount);
            _policy = policy;
            _gracePeriod = gracePeriod;
            Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        }

        public int DayCount { get; }

        public ResetSchedule Schedule { get; }

        /// <summary>No more rewards: there are no days or the <see cref="DailyRewardCyclePolicy.Once"/> cycle is done.</summary>
        public bool IsCycleComplete(DailyRewardsState state) =>
            DayCount == 0 || (_policy == DailyRewardCyclePolicy.Once && state.ClaimedCount >= DayCount);

        /// <summary>The last claim was before the start of the current reset window.</summary>
        public bool IsNewWindow(DailyRewardsState state, DateTime utcNow) =>
            state.LastClaimUtcTicks < Schedule.PeriodStart(utcNow).Ticks;

        public bool CanClaim(DailyRewardsState state, DateTime utcNow) =>
            !IsCycleComplete(state) && IsNewWindow(state, utcNow);

        /// <summary>
        /// How many days of the current cycle are claimed — as the player sees them. In
        /// <see cref="DailyRewardCyclePolicy.Loop"/> a finished cycle stays fully visible until
        /// the next reset; with the new window a new cycle starts (0).
        /// </summary>
        public int ClaimedInCycle(DailyRewardsState state, DateTime utcNow)
        {
            var claimed = Math.Min(Math.Max(state.ClaimedCount, 0), DayCount);

            if (_policy == DailyRewardCyclePolicy.Loop && claimed >= DayCount && IsNewWindow(state, utcNow))
                return 0;

            return claimed;
        }

        public static CalendarDayState DayState(int dayIndex, int claimedInCycle, bool canClaim)
        {
            if (dayIndex < claimedInCycle)
                return CalendarDayState.Claimed;

            if (dayIndex == claimedInCycle)
                return canClaim ? CalendarDayState.Claimable : CalendarDayState.Locked;

            return CalendarDayState.Future;
        }

        /// <summary>
        /// The state after a claim at <paramref name="utcNow"/>. false — nothing to claim
        /// right now; <paramref name="state"/> is never changed either way.
        /// </summary>
        public bool TryClaim(DailyRewardsState state, DateTime utcNow, out DailyRewardsState next, out ClaimOutcome outcome)
        {
            next = null;
            outcome = default;

            if (!CanClaim(state, utcNow))
                return false;

            var dayIndex = ClaimedInCycle(state, utcNow);
            if (dayIndex >= DayCount)
                return false;

            next = state.Clone();

            // The streak resets if the last claim was earlier than "window start minus
            // grace period". With one day, missing any day resets the streak.
            var streakReset = false;
            if (next.Streak > 0)
            {
                var lastClaim = new DateTime(next.LastClaimUtcTicks, DateTimeKind.Utc);
                if (lastClaim < Schedule.PeriodStart(utcNow) - _gracePeriod)
                {
                    next.Streak = 0;
                    streakReset = true;
                }
            }

            // In Loop a finished cycle starts over here: dayIndex is already 0.
            next.ClaimedCount = dayIndex + 1;
            next.Streak++;
            next.MaxConsecutiveStreak = Math.Max(next.MaxConsecutiveStreak, next.Streak);
            next.LastClaimUtcTicks = utcNow.Ticks;

            var completedCycle = next.ClaimedCount >= DayCount;
            if (completedCycle)
                next.CurrentCycleNumber++;

            outcome = new ClaimOutcome(dayIndex, streakReset, completedCycle);
            return true;
        }
    }
}
