using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.DailyRewards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.DailyRewards.Tests
{
    [TestFixture]
    public class DailyRewardsServiceTests
    {
        private sealed class SpyBridge : IDailyRewardsGameBridge
        {
            public List<DailyRewardClaimResult> ApplyRewardCalls { get; } = new();
            public bool ThrowOnApplyReward { get; set; }

            public void ApplyReward(DailyRewardClaimResult result)
            {
                ApplyRewardCalls.Add(result);
                if (ThrowOnApplyReward) throw new InvalidOperationException("ApplyReward boom");
            }
        }

        private sealed class TestConfig : IDailyRewardsConfig
        {
            public IReadOnlyList<DailyRewardDay> Days { get; set; }
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

        // Stores a copy, like a real (JSON) store — otherwise the test would see
        // state changes the service never actually saved.
        private sealed class MemoryStore : IDailyRewardsStateStore
        {
            public DailyRewardsState State { get; set; }
            public DailyRewardsStateLoadStatus? LoadStatus { get; set; }
            public bool FailSaves { get; set; }
            public TaskCompletionSource<bool> Gate { get; set; }
            public int SaveCount { get; private set; }

            public Task<DailyRewardsStateLoadResult> LoadAsync()
            {
                var result = LoadStatus switch
                {
                    DailyRewardsStateLoadStatus.Unavailable => DailyRewardsStateLoadResult.Unavailable(),
                    DailyRewardsStateLoadStatus.Unreadable => DailyRewardsStateLoadResult.Unreadable(),
                    DailyRewardsStateLoadStatus.Empty => DailyRewardsStateLoadResult.Empty(),
                    _ => State != null ? DailyRewardsStateLoadResult.Loaded(Copy(State)) : DailyRewardsStateLoadResult.Empty()
                };
                return Task.FromResult(result);
            }

            public async Task SaveAsync(DailyRewardsState state)
            {
                if (Gate != null)
                    await Gate.Task;

                if (FailSaves)
                    throw new InvalidOperationException("storage is down");

                State = Copy(state);
                SaveCount++;
            }

            private static DailyRewardsState Copy(DailyRewardsState s) => s.Clone();
        }

        private sealed class MemoryPersistentStorage : IPersistentStorage
        {
            public string Json { get; private set; }

            public Task<string> LoadAsync(string key) => Task.FromResult(Json);

            public Task SaveAsync(string key, string data)
            {
                Json = data;
                return Task.CompletedTask;
            }

            public Task<bool> HasKeyAsync(string key) => Task.FromResult(Json != null);

            public Task DeleteAsync(string key)
            {
                Json = null;
                return Task.CompletedTask;
            }
        }

        private sealed class FakeClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
            public void Advance(TimeSpan by) => UtcNow += by;
        }

        private TestConfig _config;
        private MemoryStore _store;
        private SpyBridge _bridge;
        private FakeClock _clock;
        private DailyRewardsService _service;
        private IDailyRewardsDebug _debug;

        [SetUp]
        public void Setup()
        {
            _config = new TestConfig();
            _store = new MemoryStore();
            _bridge = new SpyBridge();
            _clock = new FakeClock();
            _service = new DailyRewardsService(_config, _store, _bridge, () => _clock.UtcNow);
            _debug = _service;
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
            var result = await _service.ClaimAsync();
            Assert.That(result, Is.True, "First claim should succeed");
            Assert.That(_service.Streak, Is.EqualTo(1), "Streak should be 1 after first claim");
            Assert.That(_service.MaxConsecutiveStreak, Is.EqualTo(1), "Max streak should be 1");
            Assert.That(_store.State.ClaimedCount, Is.EqualTo(1), "Claimed day should be persisted");
        }

        [Test]
        public async Task DoubleClaimInSameWindow_Rejected()
        {
            await InitAsync();

            await _service.ClaimAsync();
            var result = await _service.ClaimAsync();
            Assert.That(result, Is.False, "Second claim in same window should be rejected");
            Assert.That(_service.Streak, Is.EqualTo(1), "Streak should stay 1");
        }

        [Test]
        public async Task SkipToNextResetWindow_EnablesClaimAgain()
        {
            await InitAsync();

            await _service.ClaimAsync();
            _debug.SkipToNextResetWindow();
            Assert.That(_service.CanClaimToday, Is.True, "After skip, should be claimable");
            var result = await _service.ClaimAsync();
            Assert.That(result, Is.True, "Claim after skip should succeed");
            Assert.That(_service.Streak, Is.EqualTo(2), "Streak should be 2 after second claim");
        }

        [Test]
        public async Task ResetWindow_FollowsInjectedClock()
        {
            _clock.UtcNow = new DateTime(2026, 1, 10, 23, 0, 0, DateTimeKind.Utc);
            await InitAsync();

            await _service.ClaimAsync();

            _clock.Advance(TimeSpan.FromMinutes(30));
            Assert.That(_service.CanClaimToday, Is.False, "23:30 is still the same window");

            _clock.Advance(TimeSpan.FromHours(1));
            Assert.That(_service.CanClaimToday, Is.True, "00:30 is past the 00:00 UTC reset");
        }

        [Test]
        public async Task NextDayClaim_KeepsStreak()
        {
            await InitAsync();

            await _service.ClaimAsync();
            _clock.Advance(TimeSpan.FromDays(1));
            await _service.ClaimAsync();

            Assert.That(_service.Streak, Is.EqualTo(2), "Consecutive days should grow the streak");
            Assert.That(_bridge.ApplyRewardCalls.Last().StreakWasReset, Is.False, "No streak reset on consecutive days");
        }

        [Test]
        public async Task GraceExceeded_ResetsStreak_PreservesCycleProgress()
        {
            await InitAsync();

            await _service.ClaimAsync();
            var claimedBefore = _store.State.ClaimedCount;
            Assert.That(claimedBefore, Is.EqualTo(1), "Should have a claimed day");

            _clock.Advance(TimeSpan.FromDays(3));

            var result = await _service.ClaimAsync();
            Assert.That(result, Is.True, "Claim should still succeed after grace reset");
            Assert.That(_service.Streak, Is.EqualTo(1), "Streak should reset to 1 (new streak)");
            Assert.That(_bridge.ApplyRewardCalls.Last().StreakWasReset, Is.True, "Result reports the streak reset");
            Assert.That(_store.State.ClaimedCount, Is.GreaterThan(claimedBefore),
                "Cycle progress should be preserved (grace only resets streak)");
        }

        [Test]
        public async Task ClaimResult_DescribesTheClaim()
        {
            await InitAsync();

            await _service.ClaimAsync();

            Assert.That(_bridge.ApplyRewardCalls, Has.Count.EqualTo(1), "ApplyReward should fire once");
            var result = _bridge.ApplyRewardCalls[0];
            Assert.That(result.Day.DayIndex, Is.EqualTo(0), "First day reward");
            Assert.That(result.Day.Rewards.Count, Is.EqualTo(1), "Day rewards are passed through");
            Assert.That((result.Streak, result.MaxStreak), Is.EqualTo((1, 1)), "Streak 1, max 1");
            Assert.That(result.StreakWasReset, Is.False);
            Assert.That(result.CompletedCycle, Is.False);
            Assert.That(result.CycleNumber, Is.EqualTo(0), "No cycle completed yet");
        }

        [Test]
        public async Task SaveFails_NoRewardAndNoProgress()
        {
            await InitAsync();
            _store.FailSaves = true;
            LogAssert.Expect(LogType.Error, new Regex("failed to save state"));

            var result = await _service.ClaimAsync();

            Assert.That(result, Is.False, "Claim must fail when the claimed day was not persisted");
            Assert.That(_bridge.ApplyRewardCalls, Is.Empty, "No reward without a persisted claim");
            Assert.That(_service.Streak, Is.EqualTo(0), "In-memory state must be rolled back");
            Assert.That(_service.CanClaimToday, Is.True, "Day must stay claimable after a failed save");
            Assert.That(_service.GetCalendar()[0].State, Is.EqualTo(CalendarDayState.Claimable), "Day 0 still claimable");

            _store.FailSaves = false;
            Assert.That(await _service.ClaimAsync(), Is.True, "Claim should succeed once storage recovers");
            Assert.That(_bridge.ApplyRewardCalls, Has.Count.EqualTo(1), "Reward granted exactly once");
            Assert.That(_bridge.ApplyRewardCalls[0].Day.DayIndex, Is.EqualTo(0), "Still day 0 — the failed claim did not consume it");
        }

        [Test]
        public async Task ClaimInFlight_SecondClaimRejected_RewardAfterSave()
        {
            await InitAsync();
            _store.Gate = new TaskCompletionSource<bool>();

            var first = _service.ClaimAsync();

            Assert.That(first.IsCompleted, Is.False, "Claim should wait for the save");
            Assert.That(_service.CanClaimToday, Is.False, "Not claimable while the save is in flight");
            Assert.That(await _service.ClaimAsync(), Is.False, "Second press during the save is rejected");
            Assert.That(_bridge.ApplyRewardCalls, Is.Empty, "No reward before the save completes");

            _store.Gate.SetResult(true);

            Assert.That(await first, Is.True, "First claim succeeds after the save");
            Assert.That(_bridge.ApplyRewardCalls, Has.Count.EqualTo(1), "Reward granted exactly once");
            Assert.That(_store.SaveCount, Is.EqualTo(1), "Exactly one save");
        }

        [Test]
        public async Task BridgeThrows_ClaimStillCompletes()
        {
            await InitAsync();
            await _service.ClaimAsync();
            _clock.Advance(TimeSpan.FromDays(3));

            _bridge.ThrowOnApplyReward = true;
            LogAssert.Expect(LogType.Error, new Regex("ApplyReward failed"));
            var changed = 0;
            _service.Changed += () => changed++;

            var result = await _service.ClaimAsync();

            Assert.That(result, Is.True, "An exception in the game bridge must not break the claim");
            Assert.That(changed, Is.EqualTo(1), "Changed still raised after the bridge throws");
            Assert.That(_store.State.Streak, Is.EqualTo(1), "Streak reset is persisted");
            Assert.That(_store.State.ClaimedCount, Is.EqualTo(2), "Claimed day is persisted");
        }

        [Test]
        public async Task StoreUnavailable_ServiceStaysOff()
        {
            _store.LoadStatus = DailyRewardsStateLoadStatus.Unavailable;
            LogAssert.Expect(LogType.Error, new Regex("state failed to load"));

            await InitAsync();

            Assert.That(_service.IsReady, Is.False, "Not ready when the store did not answer");
            Assert.That(_service.CanClaimToday, Is.False, "Nothing to claim");
            Assert.That(await _service.ClaimAsync(), Is.False, "Claim rejected");
            Assert.That(_service.GetCalendar(), Is.Empty, "No calendar");
            Assert.That(_service.TimeUntilNextClaim, Is.EqualTo(TimeSpan.Zero), "Nothing to wait for");
            Assert.That(_store.SaveCount, Is.EqualTo(0), "Must not overwrite the save it could not read");
        }

        [Test]
        public async Task StoreUnreadable_TodayWithheld_RestartsAfterReset()
        {
            _store.LoadStatus = DailyRewardsStateLoadStatus.Unreadable;

            await InitAsync();

            Assert.That(_service.IsReady, Is.True, "Calendar works after an unreadable save");
            Assert.That(_service.CanClaimToday, Is.False, "Today is withheld: it may have been claimed already");
            Assert.That(_store.SaveCount, Is.EqualTo(1), "Fresh state is written right away");
            Assert.That(_store.State.LastClaimUtcTicks, Is.EqualTo(_clock.UtcNow.Ticks), "Today marked as used");

            _store.LoadStatus = null;
            _clock.Advance(TimeSpan.FromDays(1));

            Assert.That(await _service.ClaimAsync(), Is.True, "Claimable after the next reset");
            Assert.That(_bridge.ApplyRewardCalls[0].Day.DayIndex, Is.EqualTo(0), "Calendar starts over");
            Assert.That(_service.Streak, Is.EqualTo(1), "Streak starts over");
        }

        [Test]
        public void Parse_ClassifiesSaves()
        {
            Assert.That(JsonDailyRewardsStateStore.Parse(null).Status, Is.EqualTo(DailyRewardsStateLoadStatus.Empty));
            Assert.That(JsonDailyRewardsStateStore.Parse("").Status, Is.EqualTo(DailyRewardsStateLoadStatus.Empty));
            Assert.That(JsonDailyRewardsStateStore.Parse("{not json").Status, Is.EqualTo(DailyRewardsStateLoadStatus.Unreadable));
            Assert.That(JsonDailyRewardsStateStore.Parse("{\"Version\":99}").Status, Is.EqualTo(DailyRewardsStateLoadStatus.Unreadable),
                "A newer save must not be treated as empty and overwritten");

            Assert.That(JsonDailyRewardsStateStore.Parse("null").Status, Is.EqualTo(DailyRewardsStateLoadStatus.Empty));
            Assert.That(JsonDailyRewardsStateStore.Parse("[1,2]").Status, Is.EqualTo(DailyRewardsStateLoadStatus.Unreadable));

            var loaded = JsonDailyRewardsStateStore.Parse("{\"Version\":2,\"Streak\":3,\"ClaimedCount\":4}");
            Assert.That(loaded.Status, Is.EqualTo(DailyRewardsStateLoadStatus.Loaded));
            Assert.That(loaded.State.Streak, Is.EqualTo(3));
            Assert.That(loaded.State.ClaimedCount, Is.EqualTo(4));
        }

        [Test]
        public void Parse_MigratesV1()
        {
            var v1 = "{\"Version\":1,\"ClaimedDayIndices\":[0,1,2],\"Streak\":3,\"MaxConsecutiveStreak\":5," +
                     "\"LastClaimUtcTicks\":638000000000000000,\"CurrentCycleNumber\":2}";

            var loaded = JsonDailyRewardsStateStore.Parse(v1);

            Assert.That(loaded.Status, Is.EqualTo(DailyRewardsStateLoadStatus.Loaded));
            Assert.That(loaded.State.Version, Is.EqualTo(DailyRewardsState.CurrentVersion), "Upgraded to the current version");
            Assert.That(loaded.State.ClaimedCount, Is.EqualTo(3), "Claimed days count comes from the old array");
            Assert.That(loaded.State.Streak, Is.EqualTo(3));
            Assert.That(loaded.State.MaxConsecutiveStreak, Is.EqualTo(5));
            Assert.That(loaded.State.LastClaimUtcTicks, Is.EqualTo(638000000000000000));
            Assert.That(loaded.State.CurrentCycleNumber, Is.EqualTo(2));

            Assert.That(JsonDailyRewardsStateStore.Parse("{\"Version\":1,\"ClaimedDayIndices\":null}").State.ClaimedCount,
                Is.EqualTo(0), "Null array is zero days");
            Assert.That(JsonDailyRewardsStateStore.Parse("{\"ClaimedDayIndices\":[0]}").State.ClaimedCount,
                Is.EqualTo(1), "A save without Version is v1");
        }

        [Test]
        public async Task SavedState_IsCurrentFormat()
        {
            var storage = new MemoryPersistentStorage();
            var store = new JsonDailyRewardsStateStore(storage);
            await store.SaveAsync(new DailyRewardsState { ClaimedCount = 2, Streak = 2 });

            Assert.That(storage.Json, Does.Contain("\"ClaimedCount\":2"));
            Assert.That(storage.Json, Does.Not.Contain("ClaimedDayIndices"));
            Assert.That(storage.Json, Does.Contain($"\"Version\":{DailyRewardsState.CurrentVersion}"));

            var reloaded = await store.LoadAsync();
            Assert.That(reloaded.State.ClaimedCount, Is.EqualTo(2), "Round trip");
        }

        [Test]
        public async Task Changed_RaisedOnSuccessfulClaimOnly()
        {
            await InitAsync();
            var changed = 0;
            _service.Changed += () => changed++;

            _store.FailSaves = true;
            LogAssert.Expect(LogType.Error, new Regex("failed to save state"));
            await _service.ClaimAsync();
            Assert.That(changed, Is.EqualTo(0), "Nothing changed after a failed claim");

            _store.FailSaves = false;
            await _service.ClaimAsync();
            Assert.That(changed, Is.EqualTo(1), "Changed after a successful claim");
        }

        [Test]
        public async Task Tick_RaisesChangedOncePerNewWindow()
        {
            await InitAsync();
            await _service.ClaimAsync();
            var changed = 0;
            _service.Changed += () => changed++;

            _service.Tick();
            _clock.Advance(TimeSpan.FromHours(1));
            _service.Tick();
            Assert.That(changed, Is.EqualTo(0), "Same window — no event");

            _clock.Advance(TimeSpan.FromDays(1));
            _service.Tick();
            _service.Tick();
            Assert.That(changed, Is.EqualTo(1), "New window — exactly one event");
            Assert.That(_service.CanClaimToday, Is.True, "New day is claimable");
        }

        [Test]
        public async Task TimeUntilNextClaim_CountsToNextReset()
        {
            await InitAsync();
            Assert.That(_service.TimeUntilNextClaim, Is.EqualTo(TimeSpan.Zero), "Claimable now — nothing to wait for");

            await _service.ClaimAsync();
            Assert.That(_service.TimeUntilNextClaim, Is.EqualTo(TimeSpan.FromHours(12)), "12:00 → next reset at 00:00 UTC");

            _clock.Advance(TimeSpan.FromHours(11));
            Assert.That(_service.TimeUntilNextClaim, Is.EqualTo(TimeSpan.FromHours(1)));
        }

        [Test]
        public void ConfigSO_DaysAreBuiltOnce()
        {
            var config = ScriptableObject.CreateInstance<DefaultDailyRewardConfigSO>();
            try
            {
                typeof(DefaultDailyRewardConfigSO)
                    .GetField("_days", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(config, new[] { new DailyRewardDayDef(), new DailyRewardDayDef() });

                var days = config.Days;
                Assert.That(days.Count, Is.EqualTo(2));
                Assert.That(config.Days, Is.SameAs(days), "Days must be cached, not rebuilt per call");
                Assert.That(config.Days[0], Is.SameAs(days[0]), "Same day objects on every call");
                Assert.That(days[1].DayIndex, Is.EqualTo(1), "Day index comes from the position in the list");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public async Task Config_DayIndexComesFromPosition()
        {
            var days = _config.Days.ToArray();
            days[1] = new DailyRewardDay { DayIndex = 5, Rewards = days[1].Rewards };
            days[2] = null;
            _config.Days = days;

            await InitAsync();
            var calendar = _service.GetCalendar();

            for (int i = 0; i < calendar.Length; i++)
                Assert.That(calendar[i].Day.DayIndex, Is.EqualTo(i), $"Day {i}: index is the position");
            Assert.That(calendar[1].Day.Rewards.Count, Is.EqualTo(1), "Rewards kept when the index is fixed");
            Assert.That(calendar[2].Day.Rewards, Is.Empty, "A missing day becomes a day without rewards");
        }

        [Test]
        public void Validator_AcceptsValidConfig()
        {
            var result = DailyRewardsConfigValidator.Validate(_config);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Warnings, Is.Empty);
        }

        [Test]
        public void Validator_ReportsProblems()
        {
            var days = _config.Days.ToArray();
            days[0] = new DailyRewardDay { DayIndex = 3, Rewards = days[0].Rewards };
            days[1] = new DailyRewardDay { DayIndex = 1, Rewards = Array.Empty<RewardEntry>() };
            days[2] = new DailyRewardDay { DayIndex = 2, Rewards = new[] { new RewardEntry { Amount = 5 } } };
            days[3] = new DailyRewardDay { DayIndex = 3, Rewards = new[] { new RewardEntry { Key = new RewardKey("soft") } } };
            _config.Days = days;
            _config.GracePeriod = TimeSpan.Zero;
            _config.ResetHourUtc = 25;

            var result = DailyRewardsConfigValidator.Validate(_config);

            Assert.That(result.IsValid, Is.False, "Zero grace period breaks the streak");
            Assert.That(result.Errors, Has.Some.Contains("GracePeriod"));
            Assert.That(result.Warnings, Has.Some.Contains("day 1: DayIndex = 3"));
            Assert.That(result.Warnings, Has.Some.Contains("day 2 has no rewards"));
            Assert.That(result.Warnings, Has.Some.Contains("neither key nor RewardId"));
            Assert.That(result.Warnings, Has.Some.Contains("amount 0"));
            Assert.That(result.Warnings, Has.Some.Contains("ResetHourUtc = 25"));
        }

        [Test]
        public async Task OnceMode_CompletesAfterAllDays()
        {
            _config.CyclePolicy = DailyRewardCyclePolicy.Once;
            _config.Days = Enumerable.Range(0, 7).Select(i => new DailyRewardDay
            {
                DayIndex = i,
                Rewards = Array.Empty<RewardEntry>()
            }).ToArray();

            await InitAsync();

            for (int i = 0; i < 7; i++)
            {
                Assert.That(_service.CanClaimToday, Is.True, $"Day {i} should be claimable");
                Assert.That(await _service.ClaimAsync(), Is.True, $"Claim on day {i} should succeed");
                Assert.That(_service.Streak, Is.EqualTo(i + 1), $"Streak should be {i + 1}");
                if (i < 6)
                    _debug.SkipToNextResetWindow();
            }

            Assert.That(_service.IsCycleComplete, Is.True, "Once mode should be complete after 7 days");
            Assert.That(_service.CanClaimToday, Is.False, "Should not be claimable after cycle complete");
            Assert.That(await _service.ClaimAsync(), Is.False, "Claim should fail after cycle complete");
            Assert.That(_bridge.ApplyRewardCalls.Count(r => r.CompletedCycle), Is.EqualTo(1), "Exactly one claim completes the cycle");
            var last = _bridge.ApplyRewardCalls.Last();
            Assert.That(last.CompletedCycle && last.CycleNumber == 1 && last.IsCycleComplete, Is.True,
                "Last claim completes cycle 1 for good");
        }

        [Test]
        public async Task LoopMode_WrapsAround()
        {
            _config.CyclePolicy = DailyRewardCyclePolicy.Loop;
            await InitAsync();

            for (int i = 0; i < 7; i++)
            {
                await _service.ClaimAsync();
                if (i < 6)
                    _debug.SkipToNextResetWindow();
            }

            Assert.That(_service.IsCycleComplete, Is.False, "Loop mode should never be complete");
            Assert.That(_bridge.ApplyRewardCalls.Count(r => r.CompletedCycle), Is.EqualTo(1), "One completed cycle");
            Assert.That(_bridge.ApplyRewardCalls.Last().IsCycleComplete, Is.False, "Loop never completes for good");

            _debug.SkipToNextResetWindow();
            Assert.That(_service.CanClaimToday, Is.True, "Should be claimable in new cycle");
            var result = await _service.ClaimAsync();
            Assert.That(result, Is.True, "Claim in new cycle should succeed");
            Assert.That(_bridge.ApplyRewardCalls.Last().Day.DayIndex, Is.EqualTo(0), "New cycle starts from day 0");
            Assert.That(_store.State.ClaimedCount, Is.EqualTo(1), "Previous cycle cleared on the wrap claim");
            Assert.That(_service.Streak, Is.EqualTo(8), "Streak continues across cycles");
        }

        [Test]
        public async Task LoopMode_CompletedCycleStaysVisibleUntilNextReset()
        {
            await InitAsync();
            for (int i = 0; i < 7; i++)
            {
                await _service.ClaimAsync();
                _clock.Advance(TimeSpan.FromDays(1));
            }
            _clock.Advance(TimeSpan.FromDays(-1)); // back into the window of the 7th claim

            var calendar = _service.GetCalendar();
            Assert.That(calendar.All(d => d.State == CalendarDayState.Claimed), Is.True,
                "Right after the last day the whole cycle shows as claimed");
            Assert.That(_store.State.ClaimedCount, Is.EqualTo(7), "Completed cycle is persisted as is");

            var changed = 0;
            _service.Changed += () => changed++;
            _clock.Advance(TimeSpan.FromDays(1));
            _service.Tick();

            calendar = _service.GetCalendar();
            Assert.That(changed, Is.EqualTo(1), "New window raises Changed");
            Assert.That(calendar[0].State, Is.EqualTo(CalendarDayState.Claimable), "New cycle: day 0 claimable");
            for (int i = 1; i < 7; i++)
                Assert.That(calendar[i].State, Is.EqualTo(CalendarDayState.Future), $"New cycle: day {i} future");
        }

        [Test]
        public async Task LoopMode_ShrunkDayCount_WrapsInsteadOfGettingStuck()
        {
            _store.State = DailyRewardsState.Empty();
            _store.State.ClaimedCount = 6;
            _store.State.LastClaimUtcTicks = _clock.UtcNow.AddDays(-1).Ticks;
            _config.Days = _config.Days.Take(5).ToArray();
            await InitAsync();

            Assert.That(await _service.ClaimAsync(), Is.True, "Must not get stuck when the config got shorter");
            Assert.That(_bridge.ApplyRewardCalls[0].Day.DayIndex, Is.EqualTo(0), "Starts a new cycle");
        }

        [Test]
        public async Task DisabledWithoutDays()
        {
            _config.Days = Array.Empty<DailyRewardDay>();
            await InitAsync();

            Assert.That(_service.IsReady, Is.False, "Disabled service is not ready");
            Assert.That(_service.CanClaimToday, Is.False, "Disabled without days");
            Assert.That(_service.IsCycleComplete, Is.True, "Disabled service reports cycle complete");
            Assert.That(await _service.ClaimAsync(), Is.False, "Claim should return false when disabled");
            Assert.That(_service.GetCalendar(), Is.Empty, "Calendar should be empty when disabled");
            Assert.That(_bridge.ApplyRewardCalls, Is.Empty, "No bridge calls when disabled");
            Assert.That(_service.Streak, Is.EqualTo(0), "Streak should be 0 when disabled");
        }

        [Test]
        public async Task Reset_ClearsAllState_NoBridgeCalls()
        {
            await InitAsync();
            await _service.ClaimAsync();
            _bridge.ApplyRewardCalls.Clear();

            _debug.ResetState();

            Assert.That(_service.Streak, Is.EqualTo(0), "Streak should be 0 after reset");
            Assert.That(_service.MaxConsecutiveStreak, Is.EqualTo(0), "Max streak should be 0 after reset");
            Assert.That(_service.CanClaimToday, Is.True, "Should be claimable after reset");
            Assert.That(_bridge.ApplyRewardCalls, Is.Empty, "No bridge calls on Reset");
        }

        [Test]
        public async Task Calendar_ReturnsCorrectStates()
        {
            await InitAsync();

            await _service.ClaimAsync();
            var calendar = _service.GetCalendar();

            Assert.That(calendar, Has.Length.EqualTo(7), "Calendar should have 7 days");
            Assert.That(calendar[0].State, Is.EqualTo(CalendarDayState.Claimed), "Day 0 should be Claimed");
            Assert.That(calendar[1].State, Is.EqualTo(CalendarDayState.Locked), "Day 1 should be Locked (next claim not available yet)");

            _debug.SkipToNextResetWindow();
            calendar = _service.GetCalendar();

            Assert.That(calendar[1].State, Is.EqualTo(CalendarDayState.Claimable), "Day 1 should be Claimable after skip");
            for (int i = 2; i < 7; i++)
                Assert.That(calendar[i].State, Is.EqualTo(CalendarDayState.Future), $"Day {i} should be Future");
        }

        [Test]
        public async Task InitializeAsync_WithoutDays_LogsWarning()
        {
            _config.Days = Array.Empty<DailyRewardDay>();
            LogAssert.Expect(LogType.Warning,
                "DailyRewardsService: the calendar has no days, disabling daily rewards.");

            await InitAsync();
        }
    }
}
