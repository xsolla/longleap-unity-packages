using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.DailyRewards;
using NUnit.Framework;

namespace LongLeap.DailyRewards.Tests
{
    [TestFixture]
    public class DailyRewardsServiceTests
    {
        private sealed class SpyBridge : IDailyRewardsGameBridge
        {
            public List<DailyRewardClaimResult> ApplyRewardCalls { get; } = new();
            public List<(int, int)> StreakChangedCalls { get; } = new();
            public List<int> CycleCompletedCalls { get; } = new();
            public int StreakResetCalls { get; set; }

            public void ApplyReward(DailyRewardClaimResult result) => ApplyRewardCalls.Add(result);
            public void OnStreakChanged(int streak, int maxStreak) => StreakChangedCalls.Add((streak, maxStreak));
            public void OnCycleCompleted(int cycleNumber) => CycleCompletedCalls.Add(cycleNumber);
            public void OnStreakReset() => StreakResetCalls++;
        }

        private sealed class TestConfig : IDailyRewardsConfig
        {
            public int DayCount { get; set; } = 7;
            public DailyRewardDay[] Days { get; set; }
            public DailyRewardCyclePolicy CyclePolicy { get; set; } = DailyRewardCyclePolicy.Loop;
            public int ResetHourUtc { get; set; } = 0;
            public TimeSpan GracePeriod { get; set; } = TimeSpan.FromDays(1);

            public TestConfig()
            {
                Days = Enumerable.Range(0, 7).Select(i => new DailyRewardDay
                {
                    DayIndex = i,
                    Rewards = new[]
                    {
                        new RewardEntry
                        {
                            Key = new RewardKey("xp"),
                            RewardId = new RewardId("xp_reward"),
                            Amount = 10
                        }
                    }
                }).ToArray();
            }
        }

        private sealed class MemoryStore : IDailyRewardsStateStore
        {
            public DailyRewardsState State { get; set; }

            public Task<DailyRewardsState> LoadAsync() =>
                Task.FromResult(State ?? DefaultState());

            public Task SaveAsync(DailyRewardsState state)
            {
                State = state;
                return Task.CompletedTask;
            }

            private static DailyRewardsState DefaultState() => new DailyRewardsState
            {
                Version = 1,
                ClaimedDayIndices = Array.Empty<int>(),
                Streak = 0,
                MaxConsecutiveStreak = 0,
                LastClaimUtcTicks = 0,
                CurrentCycleNumber = 0
            };
        }

        private TestConfig _config;
        private MemoryStore _store;
        private SpyBridge _bridge;
        private DailyRewardsService _service;

        [SetUp]
        public void Setup()
        {
            _config = new TestConfig();
            _store = new MemoryStore();
            _bridge = new SpyBridge();
            _service = new DailyRewardsService(_config, _store, _bridge);
        }

        [TearDown]
        public void Teardown()
        {
            _service = null;
        }

        private async Task InitAsync()
        {
            await _service.InitializeAsync();
        }

        [Test]
        public async Task FreshClaim_Succeeds()
        {
            await InitAsync();

            Assert.That(_service.CanClaimToday, Is.True, "Fresh state should be claimable");
            var result = _service.Claim();
            Assert.That(result, Is.True, "First claim should succeed");
            Assert.That(_service.Streak, Is.EqualTo(1), "Streak should be 1 after first claim");
            Assert.That(_service.MaxConsecutiveStreak, Is.EqualTo(1), "Max streak should be 1");
        }

        [Test]
        public async Task DoubleClaimInSameWindow_Rejected()
        {
            await InitAsync();

            _service.Claim();
            var result = _service.Claim();
            Assert.That(result, Is.False, "Second claim in same window should be rejected");
            Assert.That(_service.Streak, Is.EqualTo(1), "Streak should stay 1");
        }

        [Test]
        public async Task DebugSkipToNextResetWindow_EnablesClaimAgain()
        {
            await InitAsync();

            _service.Claim();
            _service.DebugSkipToNextResetWindow();
            Assert.That(_service.CanClaimToday, Is.True, "After skip, should be claimable");
            var result = _service.Claim();
            Assert.That(result, Is.True, "Claim after skip should succeed");
            Assert.That(_service.Streak, Is.EqualTo(2), "Streak should be 2 after second claim");
        }

        [Test]
        public async Task GraceExceeded_ResetsStreak_PreservesCycleProgress()
        {
            await InitAsync();

            _service.Claim();
            var claimedBefore = _store.State.ClaimedDayIndices;
            Assert.That(claimedBefore, Is.Not.Empty, "Should have claimed indices");

            _store.State.LastClaimUtcTicks = DateTime.UtcNow.AddDays(-3).Ticks;

            var result = _service.Claim();
            Assert.That(result, Is.True, "Claim should still succeed after grace reset");
            Assert.That(_service.Streak, Is.EqualTo(1), "Streak should reset to 1 (new streak)");
            Assert.That(_bridge.StreakResetCalls, Is.EqualTo(1), "OnStreakReset should fire");
            Assert.That(_store.State.ClaimedDayIndices.Length, Is.GreaterThanOrEqualTo(claimedBefore.Length),
                "Cycle progress should be preserved (grace only resets streak)");
        }

        [Test]
        public async Task BridgeCalls_FireOnClaim()
        {
            await InitAsync();

            _service.Claim();

            Assert.That(_bridge.StreakChangedCalls, Has.Count.EqualTo(1), "OnStreakChanged should fire once");
            Assert.That(_bridge.StreakChangedCalls[0], Is.EqualTo((1, 1)), "Streak 1, max 1");
            Assert.That(_bridge.ApplyRewardCalls, Has.Count.EqualTo(1), "ApplyReward should fire once");
            Assert.That(_bridge.ApplyRewardCalls[0].Day.DayIndex, Is.EqualTo(0), "First day reward");
        }

        [Test]
        public async Task OnceMode_CompletesAfterAllDays()
        {
            _config.CyclePolicy = DailyRewardCyclePolicy.Once;
            _config.DayCount = 7;
            _config.Days = Enumerable.Range(0, 7).Select(i => new DailyRewardDay
            {
                DayIndex = i,
                Rewards = Array.Empty<RewardEntry>()
            }).ToArray();

            await InitAsync();

            for (int i = 0; i < 7; i++)
            {
                Assert.That(_service.CanClaimToday, Is.True, $"Day {i} should be claimable");
                Assert.That(_service.Claim(), Is.True, $"Claim on day {i} should succeed");
                Assert.That(_service.Streak, Is.EqualTo(i + 1), $"Streak should be {i + 1}");
                if (i < 6)
                    _service.DebugSkipToNextResetWindow();
            }

            Assert.That(_service.IsCycleComplete, Is.True, "Once mode should be complete after 7 days");
            Assert.That(_service.CanClaimToday, Is.False, "Should not be claimable after cycle complete");
            Assert.That(_service.Claim(), Is.False, "Claim should fail after cycle complete");
            Assert.That(_bridge.CycleCompletedCalls, Has.Count.EqualTo(1), "OnCycleCompleted should fire once");
            Assert.That(_bridge.CycleCompletedCalls[0], Is.EqualTo(1), "Cycle number 1");
        }

        [Test]
        public async Task LoopMode_WrapsAround()
        {
            _config.CyclePolicy = DailyRewardCyclePolicy.Loop;
            await InitAsync();

            for (int i = 0; i < 7; i++)
            {
                _service.Claim();
                if (i < 6)
                    _service.DebugSkipToNextResetWindow();
            }

            Assert.That(_service.IsCycleComplete, Is.False, "Loop mode should never be complete");
            Assert.That(_bridge.CycleCompletedCalls, Has.Count.EqualTo(1), "OnCycleCompleted should fire once per cycle");

            _service.DebugSkipToNextResetWindow();
            Assert.That(_service.CanClaimToday, Is.True, "Should be claimable in new cycle");
            var result = _service.Claim();
            Assert.That(result, Is.True, "Claim in new cycle should succeed");
        }

        [Test]
        public async Task DisabledOnZeroDayCount()
        {
            _config.DayCount = 0;
            await InitAsync();

            Assert.That(_service.CanClaimToday, Is.False, "Disabled when DayCount = 0");
            Assert.That(_service.IsCycleComplete, Is.True, "Disabled service reports cycle complete");
            Assert.That(_service.Claim(), Is.False, "Claim should return false when disabled");
            Assert.That(_service.GetCalendar(), Is.Empty, "Calendar should be empty when disabled");
            Assert.That(_bridge.ApplyRewardCalls, Is.Empty, "No bridge calls when disabled");
            Assert.That(_service.Streak, Is.EqualTo(0), "Streak should be 0 when disabled");
        }

        [Test]
        public async Task Reset_ClearsAllState_NoBridgeCalls()
        {
            await InitAsync();
            _service.Claim();
            _bridge.StreakChangedCalls.Clear();
            _bridge.ApplyRewardCalls.Clear();

            _service.Reset();

            Assert.That(_service.Streak, Is.EqualTo(0), "Streak should be 0 after reset");
            Assert.That(_service.MaxConsecutiveStreak, Is.EqualTo(0), "Max streak should be 0 after reset");
            Assert.That(_service.CanClaimToday, Is.True, "Should be claimable after reset");
            Assert.That(_bridge.StreakChangedCalls, Is.Empty, "No bridge calls on Reset");
            Assert.That(_bridge.ApplyRewardCalls, Is.Empty, "No bridge calls on Reset");
        }

        [Test]
        public async Task Calendar_ReturnsCorrectStates()
        {
            await InitAsync();

            _service.Claim();
            var calendar = _service.GetCalendar();

            Assert.That(calendar, Has.Length.EqualTo(7), "Calendar should have 7 days");
            Assert.That(calendar[0].State, Is.EqualTo(CalendarDayState.Claimed), "Day 0 should be Claimed");
            Assert.That(calendar[1].State, Is.EqualTo(CalendarDayState.Locked), "Day 1 should be Locked (next claim not available yet)");

            _service.DebugSkipToNextResetWindow();
            calendar = _service.GetCalendar();

            Assert.That(calendar[1].State, Is.EqualTo(CalendarDayState.Claimable), "Day 1 should be Claimable after skip");
            for (int i = 2; i < 7; i++)
                Assert.That(calendar[i].State, Is.EqualTo(CalendarDayState.Future), $"Day {i} should be Future");
        }

        [Test]
        public async Task InitializeAsync_WithDayCountZero_LogsWarning()
        {
            _config.DayCount = 0;
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                "DailyRewardsService: DayCount = 0, отключаю ежедневные награды.");

            await InitAsync();
        }
    }
}
