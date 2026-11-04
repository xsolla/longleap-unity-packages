using System;
using System.Threading.Tasks;

namespace LongLeap.DailyRewards
{
    public interface IDailyRewardsService
    {
        /// <summary>
        /// Something changed: a day was claimed, the reset window changed (see <see cref="Tick"/>),
        /// progress was reset. The UI just re-reads <see cref="GetCalendar"/> in response.
        /// </summary>
        event Action Changed;

        Task InitializeAsync();

        /// <summary>
        /// State is loaded and the calendar works. false — the store did not answer
        /// (the service is off until restart) or InitializeAsync has not finished yet.
        /// </summary>
        bool IsReady { get; }

        bool CanClaimToday { get; }
        bool IsCycleComplete { get; }
        int Streak { get; }
        int MaxConsecutiveStreak { get; }

        /// <summary>
        /// Time left until the next reward — for a UI countdown. <see cref="TimeSpan.Zero"/>
        /// when a reward can be claimed right now or there is nothing to wait for (the service
        /// is not ready, the <see cref="DailyRewardCyclePolicy.Once"/> cycle is done).
        /// </summary>
        TimeSpan TimeUntilNextClaim { get; }

        /// <summary>
        /// Check whether a new reset window has started and, if so, raise
        /// <see cref="Changed"/>. Cheap: call it from Update while the rewards window is open —
        /// otherwise the UI notices the new day only on the next access.
        /// </summary>
        void Tick();

        /// <summary>
        /// Claim today's reward. The reward is granted through the bridge only after
        /// the claimed day is saved; if the save fails — false and nothing is granted.
        /// While the save is in flight <see cref="CanClaimToday"/> is false.
        /// </summary>
        Task<bool> ClaimAsync();

        /// <summary>
        /// Days of the current cycle. In <see cref="DailyRewardCyclePolicy.Loop"/> a finished cycle
        /// is shown in full (all days Claimed) until the next reset, then a new cycle.
        /// </summary>
        CalendarDay[] GetCalendar();
    }
}
