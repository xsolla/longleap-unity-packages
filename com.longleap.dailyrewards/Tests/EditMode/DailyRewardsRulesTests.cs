using System;
using LongLeap.Core;
using NUnit.Framework;

namespace LongLeap.DailyRewards.Tests
{
    [TestFixture]
    public class DailyRewardsRulesTests
    {
        private static readonly DateTime Noon = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        private static DailyRewardsRules Rules(int days = 3, DailyRewardCyclePolicy policy = DailyRewardCyclePolicy.Loop,
            double graceDays = 1) =>
            new DailyRewardsRules(days, policy, TimeSpan.FromDays(graceDays), new ResetSchedule(0));

        private static DailyRewardsState Claimed(int count, DateTime lastClaim, int streak = 1) => new DailyRewardsState
        {
            ClaimedCount = count,
            Streak = streak,
            MaxConsecutiveStreak = streak,
            LastClaimUtcTicks = lastClaim.Ticks
        };

        [Test]
        public void TryClaim_DoesNotChangeInputState()
        {
            var state = DailyRewardsState.Empty();

            Assert.That(Rules().TryClaim(state, Noon, out var next, out var outcome), Is.True);

            Assert.That(state.ClaimedCount, Is.EqualTo(0), "Input state untouched");
            Assert.That(state.LastClaimUtcTicks, Is.EqualTo(0));
            Assert.That(next, Is.Not.SameAs(state));
            Assert.That(next.ClaimedCount, Is.EqualTo(1));
            Assert.That(next.Streak, Is.EqualTo(1));
            Assert.That(next.LastClaimUtcTicks, Is.EqualTo(Noon.Ticks));
            Assert.That(outcome.DayIndex, Is.EqualTo(0));
        }

        [Test]
        public void TryClaim_SameWindow_Rejected()
        {
            var state = Claimed(1, Noon.AddHours(-1));

            Assert.That(Rules().TryClaim(state, Noon, out var next, out _), Is.False);
            Assert.That(next, Is.Null);
        }

        [Test]
        public void TryClaim_MissedDay_ResetsStreak_KeepsProgress()
        {
            var state = Claimed(2, Noon.AddDays(-2), streak: 2);

            Rules().TryClaim(state, Noon, out var next, out var outcome);

            Assert.That(outcome.StreakWasReset, Is.True, "Default grace (1 day): a missed day breaks the streak");
            Assert.That(next.Streak, Is.EqualTo(1));
            Assert.That(next.MaxConsecutiveStreak, Is.EqualTo(2), "Max streak kept");
            Assert.That(outcome.DayIndex, Is.EqualTo(2), "Cycle progress kept");
        }

        [Test]
        public void TryClaim_TwoDayGrace_ForgivesOneMissedDay()
        {
            var state = Claimed(2, Noon.AddDays(-2), streak: 2);

            Rules(graceDays: 2).TryClaim(state, Noon, out var next, out var outcome);

            Assert.That(outcome.StreakWasReset, Is.False);
            Assert.That(next.Streak, Is.EqualTo(3));
        }

        [Test]
        public void TryClaim_LastDay_CompletesCycle()
        {
            Rules().TryClaim(Claimed(2, Noon.AddDays(-1)), Noon, out var next, out var outcome);

            Assert.That(outcome.CompletedCycle, Is.True);
            Assert.That(next.CurrentCycleNumber, Is.EqualTo(1));
            Assert.That(next.ClaimedCount, Is.EqualTo(3), "Loop keeps the finished cycle until the next window");
        }

        [Test]
        public void Loop_FinishedCycle_VisibleUntilReset_ThenStartsOver()
        {
            var rules = Rules();
            var state = Claimed(3, Noon);

            Assert.That(rules.ClaimedInCycle(state, Noon.AddHours(1)), Is.EqualTo(3), "Same window: all claimed");
            Assert.That(rules.ClaimedInCycle(state, Noon.AddDays(1)), Is.EqualTo(0), "Next window: new cycle");

            rules.TryClaim(state, Noon.AddDays(1), out var next, out var outcome);
            Assert.That(outcome.DayIndex, Is.EqualTo(0));
            Assert.That(next.ClaimedCount, Is.EqualTo(1));
        }

        [Test]
        public void Once_FinishedCycle_IsComplete()
        {
            var rules = Rules(policy: DailyRewardCyclePolicy.Once);
            var state = Claimed(3, Noon.AddDays(-5));

            Assert.That(rules.IsCycleComplete(state), Is.True);
            Assert.That(rules.CanClaim(state, Noon), Is.False);
            Assert.That(rules.ClaimedInCycle(state, Noon), Is.EqualTo(3), "All days stay claimed");
        }

        [Test]
        public void ClaimedInCycle_ClampsToDayCount()
        {
            Assert.That(Rules().ClaimedInCycle(Claimed(5, Noon), Noon), Is.EqualTo(3), "Config got shorter");
            Assert.That(Rules().ClaimedInCycle(Claimed(-1, Noon), Noon), Is.EqualTo(0), "Corrupted negative count");
        }

        [TestCase(0, 2, true, CalendarDayState.Claimed)]
        [TestCase(2, 2, true, CalendarDayState.Claimable)]
        [TestCase(2, 2, false, CalendarDayState.Locked)]
        [TestCase(3, 2, true, CalendarDayState.Future)]
        public void DayState(int day, int claimed, bool canClaim, CalendarDayState expected)
        {
            Assert.That(DailyRewardsRules.DayState(day, claimed, canClaim), Is.EqualTo(expected));
        }
    }
}
