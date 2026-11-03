using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.Stats;
using LongLeap.Stats.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.DailyQuests.Tests
{
    [TestFixture]
    public class DailyQuestServiceTests
    {
        private sealed class MemoryStorage : IPersistentStorage
        {
            public readonly Dictionary<string, string> Data = new();
            public Task<string> LoadAsync(string key) => Task.FromResult(Data.TryGetValue(key, out var v) ? v : null);
            public Task SaveAsync(string key, string data) { Data[key] = data; return Task.CompletedTask; }
            public Task<bool> HasKeyAsync(string key) => Task.FromResult(Data.ContainsKey(key));
            public Task DeleteAsync(string key) { Data.Remove(key); return Task.CompletedTask; }
        }

        private sealed class UnavailableStore : IDailyQuestStateStore
        {
            public Task<DailyQuestStateLoadResult> LoadAsync() => Task.FromResult(DailyQuestStateLoadResult.Unavailable());
            public Task SaveAsync(DailyQuestsState state) => Task.CompletedTask;
        }

        /// <summary>A write hangs until <see cref="Gate"/> is opened.</summary>
        private sealed class GatedStore : IDailyQuestStateStore
        {
            public TaskCompletionSource<bool> Gate;
            public Task<DailyQuestStateLoadResult> LoadAsync() => Task.FromResult(DailyQuestStateLoadResult.Empty());
            public async Task SaveAsync(DailyQuestsState state)
            {
                if (Gate != null) await Gate.Task;
            }
        }

        private sealed class FailingSaveStore : IDailyQuestStateStore
        {
            public bool Fail;
            public Task<DailyQuestStateLoadResult> LoadAsync() => Task.FromResult(DailyQuestStateLoadResult.Empty());
            public Task SaveAsync(DailyQuestsState state) =>
                Fail ? Task.FromException(new InvalidOperationException("disk full")) : Task.CompletedTask;
        }

        private sealed class ConfigProvider : IDailyQuestProvider
        {
            public DailyQuestConfig Config;
            public Task<DailyQuestConfig> LoadAsync() => Task.FromResult(Config);
        }

        private sealed class SpyBridge : IDailyQuestGameBridge
        {
            public readonly List<QuestStatus[]> Rotations = new();
            public readonly List<QuestStatus> Completed = new();
            public readonly List<QuestClaimResult> Claims = new();
            public void OnQuestsRotated(QuestStatus[] active) => Rotations.Add(active);
            public void OnQuestCompleted(QuestStatus status) => Completed.Add(status);
            public void ApplyRewards(QuestClaimResult result) => Claims.Add(result);
        }

        private static readonly StatPath Grind = new("grind");
        private static readonly StatPath Ollie = new("ollie");
        private static readonly StatPath RunScore = new("run_score");

        private DateTime _now;
        private FakeStatSystem _stats;
        private MemoryStorage _storage;
        private ConfigProvider _provider;
        private SpyBridge _bridge;
        private DailyQuestService _service;

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            _stats = new FakeStatSystem();
            _stats.Define(Grind, StatType.Value);
            _stats.Define(Ollie, StatType.Value);
            _stats.Define(RunScore, StatType.Value);
            _storage = new MemoryStorage();
            _bridge = new SpyBridge();
            _provider = new ConfigProvider
            {
                Config = Config(1,
                    Quest("grind_50", "grind", 50, 5),
                    Quest("ollie_20", "ollie", 20, 3))
            };
        }

        [TearDown]
        public void Teardown() => _service?.Shutdown();

        // --- helpers --------------------------------------------------------

        private static QuestDefinition Quest(string id, string stat, int target, int soft,
            int weight = 1, QuestProgressMode mode = QuestProgressMode.Delta) =>
            new QuestDefinition
            {
                Id = new QuestId(id),
                Weight = weight,
                Objectives = new[]
                {
                    new QuestObjective { Id = new QuestObjectiveId("o0"), StatPath = stat, StatTarget = target, Mode = mode }
                },
                Rewards = new[] { new QuestReward { Key = new QuestRewardKey("soft"), Amount = soft } }
            };

        /// <summary>"Score 100 points in a run + 3 grinds".</summary>
        private static QuestDefinition ScoreAndGrind(string id) =>
            new QuestDefinition
            {
                Id = new QuestId(id),
                Objectives = new[]
                {
                    new QuestObjective { Id = new QuestObjectiveId("score"), StatPath = "run_score", StatTarget = 100, Mode = QuestProgressMode.Absolute },
                    new QuestObjective { Id = new QuestObjectiveId("grind"), StatPath = "grind", StatTarget = 3 }
                },
                Rewards = new[] { new QuestReward { Key = new QuestRewardKey("soft"), Amount = 1 } }
            };

        private static DailyQuestConfig Config(int dailyCount, params QuestDefinition[] quests) =>
            new DailyQuestConfig
            {
                Settings = new DailyQuestSettings { DailyCount = dailyCount, ResetHourUtc = 0 },
                Quests = quests
            };

        private async Task<DailyQuestService> Start(IDailyQuestStateStore store = null)
        {
            _service?.Shutdown();
            _service = new DailyQuestService(
                _provider,
                store ?? new JsonDailyQuestStateStore(_storage),
                _bridge,
                _stats,
                () => _now);
            await _service.InitializeAsync();
            return _service;
        }

        private QuestStatus Only() => _service.GetActive().Single();

        private IDailyQuestDebug DebugApi => _service;

        // --- issuing ------------------------------------------------------------

        [Test]
        public async Task Initialize_IssuesDailyCountQuests()
        {
            _provider.Config = Config(2, Quest("a", "grind", 5, 1), Quest("b", "ollie", 5, 1), Quest("c", "grind", 9, 1));

            await Start();

            Assert.AreEqual(2, _service.GetActive().Length);
            Assert.AreEqual(1, _bridge.Rotations.Count);
        }

        [Test]
        public async Task Initialize_PoolSmallerThanDailyCount_IssuesWholePool()
        {
            _provider.Config = Config(5, Quest("a", "grind", 5, 1));

            await Start();

            Assert.AreEqual(1, _service.GetActive().Length);
        }

        [Test]
        public async Task Initialize_ZeroWeightQuest_IsNeverIssued()
        {
            _provider.Config = Config(2, Quest("on", "grind", 5, 1), Quest("off", "ollie", 5, 1, weight: 0));

            await Start();

            CollectionAssert.AreEqual(new[] { "on" }, _service.GetActive().Select(s => s.Id.Value).ToArray());
        }

        // --- progress -----------------------------------------------------------

        [Test]
        public async Task DeltaProgress_CountsOnlySinceQuestWasIssued()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            _stats.Set(Grind, 1000);

            await Start();
            _stats.Add(Grind, 10);

            var status = Only();
            Assert.AreEqual(new QuestProgress(10, 50), status.TotalProgress);
            Assert.AreEqual(QuestState.InProgress, status.State);
        }

        [Test]
        public async Task AbsoluteProgress_UsesStatValueAsIs()
        {
            _provider.Config = Config(1, Quest("score", "run_score", 100, 5, mode: QuestProgressMode.Absolute));
            _stats.Set(RunScore, 40);

            await Start();

            Assert.AreEqual(new QuestProgress(40, 100), Only().TotalProgress);
        }

        [Test]
        public async Task ReachingTarget_CompletesQuest_ButDoesNotGrantReward()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();

            _stats.Add(Grind, 50);

            Assert.AreEqual(QuestState.Completed, Only().State);
            Assert.IsTrue(Only().CanClaim);
            Assert.AreEqual(1, _bridge.Completed.Count);
            Assert.AreEqual(0, _bridge.Claims.Count, "the reward must not be granted by itself");
            Assert.AreEqual(1, _service.ClaimableCount);
        }

        [Test]
        public async Task Progress_IsClampedToTarget()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();

            _stats.Add(Grind, 70);

            Assert.AreEqual(new QuestProgress(50, 50), Only().TotalProgress);
        }

        [Test]
        public async Task Completion_IsLatched_EvenIfStatDrops()
        {
            _provider.Config = Config(1, Quest("score", "run_score", 100, 5, mode: QuestProgressMode.Absolute));
            await Start();

            _stats.Set(RunScore, 120);
            _stats.Set(RunScore, 0);

            Assert.AreEqual(QuestState.Completed, Only().State);
        }

        [Test]
        public async Task MultiObjective_EachObjectiveIsLatched()
        {
            _provider.Config = Config(1, ScoreAndGrind("combo"));
            await Start();

            _stats.Set(RunScore, 120); // points scored in one run
            _stats.Set(RunScore, 0);   // a new run — the score was reset
            _stats.Add(Grind, 3);      // grinds finished in that run

            Assert.AreEqual(QuestState.Completed, Only().State);
        }

        [Test]
        public async Task LatchedObjective_ShowsFullProgress_AfterStatDrops()
        {
            _provider.Config = Config(1, ScoreAndGrind("combo"));
            await Start();

            _stats.Set(RunScore, 120);
            _stats.Set(RunScore, 0);

            var status = Only();
            Assert.AreEqual(QuestState.InProgress, status.State);
            Assert.AreEqual(new QuestProgress(100, 100), status.ObjectiveProgresses[0].Progress);
            Assert.AreEqual(new QuestProgress(0, 3), status.ObjectiveProgresses[1].Progress);
        }

        [Test]
        public async Task LatchedObjective_SurvivesRestart()
        {
            _provider.Config = Config(1, ScoreAndGrind("combo"));
            await Start();
            _stats.Set(RunScore, 120);
            _stats.Set(RunScore, 0);

            await Start();
            _stats.Add(Grind, 3);

            Assert.AreEqual(QuestState.Completed, Only().State);
        }

        [Test]
        public async Task StatChange_RaisesChanged()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            int raised = 0;
            _service.Changed += () => raised++;

            _stats.Add(Grind, 1);

            Assert.AreEqual(1, raised);
        }

        // --- rewards ------------------------------------------------------------

        [Test]
        public async Task Claim_CompletedQuest_AppliesRewardsOnce()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 50);

            Assert.IsTrue(await _service.ClaimAsync(new QuestId("grind_50")));
            Assert.IsFalse(await _service.ClaimAsync(new QuestId("grind_50")));

            Assert.AreEqual(1, _bridge.Claims.Count);
            Assert.AreEqual("soft", _bridge.Claims[0].Rewards[0].Key.Value);
            Assert.AreEqual(5, _bridge.Claims[0].Rewards[0].Amount);
            Assert.AreEqual(QuestState.Claimed, Only().State);
            Assert.AreEqual(0, _service.ClaimableCount);
        }

        [Test]
        public async Task Claim_UnfinishedQuest_ReturnsFalse()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 49);

            Assert.IsFalse(await _service.ClaimAsync(new QuestId("grind_50")));
            Assert.AreEqual(0, _bridge.Claims.Count);
        }

        [Test]
        public async Task Claim_UnfinishedQuest_StillReportsOtherQuestsCompleted()
        {
            _provider.Config = Config(2, Quest("grind_50", "grind", 50, 5), Quest("ollie_20", "ollie", 20, 3));
            await Start();
            _stats.SetAggregate(Ollie, 20); // the stat grew without an event
            int raised = 0;
            _service.Changed += () => raised++;

            Assert.IsFalse(await _service.ClaimAsync(new QuestId("grind_50")));

            Assert.AreEqual(1, raised);
            Assert.AreEqual(QuestState.Completed, _service.GetStatus(new QuestId("ollie_20")).State);
        }

        [Test]
        public async Task Claim_GrantsRewardOnlyAfterClaimIsSaved()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            var store = new GatedStore();
            await Start(store);
            _stats.Add(Grind, 50);

            store.Gate = new TaskCompletionSource<bool>();
            var claim = _service.ClaimAsync(new QuestId("grind_50"));

            Assert.IsFalse(claim.IsCompleted);
            Assert.AreEqual(0, _bridge.Claims.Count, "the reward must not arrive before the write");
            Assert.IsFalse(await _service.ClaimAsync(new QuestId("grind_50")), "a second press while writing");

            store.Gate.SetResult(true);

            Assert.IsTrue(await claim);
            Assert.AreEqual(1, _bridge.Claims.Count);
        }

        [Test]
        public async Task Claim_SaveFailed_GrantsNothing_AndCanBeRetried()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            var store = new FailingSaveStore();
            await Start(store);
            _stats.Add(Grind, 50);

            store.Fail = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("failed to save state"));

            Assert.IsFalse(await _service.ClaimAsync(new QuestId("grind_50")));
            Assert.AreEqual(0, _bridge.Claims.Count);
            Assert.AreEqual(QuestState.Completed, Only().State);

            store.Fail = false;

            Assert.IsTrue(await _service.ClaimAsync(new QuestId("grind_50")));
            Assert.AreEqual(1, _bridge.Claims.Count);
        }

        [Test]
        public async Task Claim_QuestNotActive_ReturnsFalse()
        {
            await Start();

            Assert.IsFalse(await _service.ClaimAsync(new QuestId("nope")));
            Assert.IsFalse(await _service.ClaimAsync(QuestId.None));
        }

        [Test]
        public async Task ForceComplete_MakesQuestClaimable()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();

            DebugApi.ForceComplete(new QuestId("grind_50"));

            Assert.IsTrue(await _service.ClaimAsync(new QuestId("grind_50")));
        }

        // --- persistence --------------------------------------------------------

        [Test]
        public async Task Restart_SamePeriod_KeepsSetAndBaseline()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            _stats.Set(Grind, 100);
            await Start();
            _stats.Add(Grind, 20);

            await Start();

            Assert.AreEqual(new QuestProgress(20, 50), Only().TotalProgress);
            Assert.AreEqual(1, _bridge.Rotations.Count, "a restart on the same day does not issue a new set");
        }

        [Test]
        public async Task Restart_ClaimedQuest_StaysClaimed()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 50);
            await _service.ClaimAsync(new QuestId("grind_50"));

            await Start();

            Assert.AreEqual(QuestState.Claimed, Only().State);
            Assert.IsFalse(await _service.ClaimAsync(new QuestId("grind_50")));
            Assert.AreEqual(1, _bridge.Claims.Count);
        }

        [Test]
        public async Task Restart_CompletedWhileOffline_IsDetectedOnInit()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _service.Shutdown();
            _stats.Add(Grind, 60);

            await Start();

            Assert.AreEqual(QuestState.Completed, Only().State);
        }

        [Test]
        public async Task Restart_QuestRemovedFromConfig_IsReplaced()
        {
            _provider.Config = Config(1, Quest("old", "grind", 50, 5));
            await Start();

            _provider.Config = Config(1, Quest("new", "ollie", 10, 5));
            await Start();

            Assert.AreEqual("new", Only().Id.Value);
        }

        [Test]
        public async Task UnavailableStore_DisablesService()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("state could not be loaded"));

            await Start(new UnavailableStore());

            Assert.IsEmpty(_service.GetActive());
            Assert.IsFalse(await _service.ClaimAsync(new QuestId("grind_50")));
        }

        [Test]
        public async Task UnreadableSave_NoQuestsUntilNextReset()
        {
            _storage.Data[JsonDailyQuestStateStore.DefaultStorageKey] = "{ broken";

            await Start();
            Assert.IsEmpty(_service.GetActive(), "what was claimed today is unknown");

            await Start();
            Assert.IsEmpty(_service.GetActive(), "a restart on the same day does not refill the set");

            _now = _now.AddDays(1);
            Assert.AreEqual(1, _service.GetActive().Length);
        }

        // --- period change ------------------------------------------------------

        [Test]
        public async Task Tick_AfterReset_IssuesNewSetAndRaisesChanged()
        {
            await Start();
            int raised = 0;
            _service.Changed += () => raised++;

            _service.Tick();
            Assert.AreEqual(0, raised, "same period — nothing happens");

            _now = _now.AddDays(1);
            _service.Tick();

            Assert.AreEqual(1, raised);
            Assert.AreEqual(2, _bridge.Rotations.Count);
        }

        [Test]
        public async Task NewPeriod_IssuesNewSet_AndResetsBaseline()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 30);

            _now = _now.AddDays(1);

            var status = Only();
            Assert.AreEqual(new QuestProgress(0, 50), status.TotalProgress);
            Assert.AreEqual(2, _bridge.Rotations.Count);
        }

        [Test]
        public async Task NewPeriod_UnclaimedRewardIsLost()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 50);

            _now = _now.AddDays(1);

            Assert.AreEqual(QuestState.InProgress, Only().State);
            Assert.IsFalse(await _service.ClaimAsync(new QuestId("grind_50")));
        }

        [Test]
        public async Task NewPeriod_PrefersQuestsNotIssuedYesterday()
        {
            _provider.Config = Config(1, Quest("a", "grind", 5, 1), Quest("b", "ollie", 5, 1));
            await Start();
            var yesterday = Only().Id;

            _now = _now.AddDays(1);

            Assert.AreNotEqual(yesterday, Only().Id);
        }

        [Test]
        public async Task ClockMovedBack_DoesNotRotate()
        {
            await Start();

            _now = _now.AddDays(-3);
            _service.GetActive();

            Assert.AreEqual(1, _bridge.Rotations.Count);
        }

        [Test]
        public async Task ResetHour_PeriodChangesAtThatHour()
        {
            _provider.Config = new DailyQuestConfig
            {
                Settings = new DailyQuestSettings { DailyCount = 1, ResetHourUtc = 18 },
                Quests = new[] { Quest("a", "grind", 5, 1) }
            };
            await Start();

            _now = _now.AddHours(5); // 17:00 — same period
            _service.GetActive();
            Assert.AreEqual(1, _bridge.Rotations.Count);

            _now = _now.AddHours(1); // 18:00 — a new one
            _service.GetActive();
            Assert.AreEqual(2, _bridge.Rotations.Count);
        }

        [Test]
        public async Task TimeUntilReset_PointsToNextResetHour()
        {
            await Start();

            Assert.AreEqual(TimeSpan.FromHours(12), _service.TimeUntilReset);
        }

        [Test]
        public async Task DebugSkip_IssuesNewSetImmediately()
        {
            await Start();

            DebugApi.SkipToNextPeriod();

            Assert.AreEqual(2, _bridge.Rotations.Count);
        }

        [Test]
        public async Task DebugResetState_ForgetsClaims_AndIssuesNewSet()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 50);
            await _service.ClaimAsync(new QuestId("grind_50"));

            DebugApi.ResetState();

            Assert.AreEqual(QuestState.InProgress, Only().State);
            Assert.AreEqual(2, _bridge.Rotations.Count);
        }

        // --- required bridge ----------------------------------------------------

        [Test]
        public void Constructor_WithoutBridge_Throws()
        {
            // Without the bridge a quest would be marked claimed and the reward lost.
            Assert.Throws<ArgumentNullException>(() =>
                new DailyQuestService(_provider, new JsonDailyQuestStateStore(_storage), bridge: null, _stats));
        }

        // --- stat rollback ------------------------------------------------------

        [Test]
        public async Task DeltaProgress_StatRolledBack_CountsFromNewValue()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            _stats.Set(Grind, 100);
            await Start();
            _stats.Add(Grind, 20);

            _stats.Set(Grind, 40); // the stats save turned out older than the quests save
            Assert.AreEqual(new QuestProgress(0, 50), Only().TotalProgress);

            _stats.Add(Grind, 10);
            Assert.AreEqual(new QuestProgress(10, 50), Only().TotalProgress,
                "progress used to stay at zero until the stat got back to 100");
        }

        [Test]
        public async Task StatRolledBack_NewBaselineSurvivesRestart()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            _stats.Set(Grind, 100);
            await Start();
            _stats.Set(Grind, 40);
            _stats.Add(Grind, 10);

            await Start();

            Assert.AreEqual(new QuestProgress(10, 50), Only().TotalProgress);
        }

        [Test]
        public async Task StatRolledBackWhileOffline_IsRebasedOnInit()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            _stats.Set(Grind, 100);
            await Start();
            _service.Shutdown();
            _stats.Set(Grind, 40);

            await Start();
            _stats.Add(Grind, 5);

            Assert.AreEqual(new QuestProgress(5, 50), Only().TotalProgress);
        }

        // --- snapshots and events -----------------------------------------------

        [Test]
        public async Task GetActive_NothingChanged_ReturnsSameSnapshot()
        {
            await Start();

            var first = _service.GetActive();
            var second = _service.GetActive();

            Assert.AreSame(first, second);
            Assert.AreSame(first[0], second[0]);
        }

        [Test]
        public async Task GetActive_AfterProgress_ReturnsNewSnapshot()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            var before = _service.GetActive();

            _stats.Add(Grind, 1);
            var after = _service.GetActive();

            Assert.AreNotSame(before, after);
            Assert.AreEqual(new QuestProgress(0, 50), before[0].TotalProgress, "the old snapshot does not change");
            Assert.AreEqual(new QuestProgress(1, 50), after[0].TotalProgress);
        }

        [Test]
        public async Task GetActive_StatChangedWithoutEvent_ShowsFreshProgress()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();

            _stats.SetAggregate(Grind, 7);

            Assert.AreEqual(new QuestProgress(7, 50), Only().TotalProgress);
        }

        [Test]
        public async Task StatChange_ProgressUnchanged_DoesNotRaiseChanged()
        {
            _provider.Config = Config(1, Quest("score", "run_score", 100, 5, mode: QuestProgressMode.Absolute));
            _stats.Set(RunScore, 40);
            await Start();
            int raised = 0;
            _service.Changed += () => raised++;

            _stats.Set(RunScore, 40);

            Assert.AreEqual(0, raised);
        }

        [Test]
        public async Task StatChange_AfterQuestCompleted_DoesNotRaiseChanged()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 50);
            int raised = 0;
            _service.Changed += () => raised++;

            _stats.Add(Grind, 10);

            Assert.AreEqual(0, raised);
        }

        // --- selection rules ----------------------------------------------------

        private static QuestDefinition Ranked(string id, int difficulty, string category) =>
            new QuestDefinition
            {
                Id = new QuestId(id),
                Difficulty = difficulty,
                Categories = new[] { category },
                Objectives = new[] { new QuestObjective { Id = new QuestObjectiveId("o0"), StatPath = "grind", StatTarget = 10 } },
                Rewards = new[] { new QuestReward { Key = new QuestRewardKey("soft"), Amount = 1 } }
            };

        private static DailyQuestConfig RankedConfig(params QuestDefinition[] quests) =>
            new DailyQuestConfig
            {
                Settings = new DailyQuestSettings
                {
                    DailyCount = 3, UniqueCategories = true, HardestLast = true, SlotDifficulties = new[] { 1, 2, 3 }
                },
                Quests = quests
            };

        [Test]
        public async Task SelectionRules_EveryDay_SetFollowsSlotsAndCategories()
        {
            _provider.Config = RankedConfig(
                Ranked("a1", 1, "a"), Ranked("a2", 2, "a"), Ranked("a3", 3, "a"),
                Ranked("b1", 1, "b"), Ranked("b2", 2, "b"), Ranked("b3", 3, "b"),
                Ranked("c1", 1, "c"), Ranked("c2", 2, "c"), Ranked("c3", 3, "c"));
            await Start();

            for (int day = 0; day < 30; day++)
            {
                var active = _service.GetActive();
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, active.Select(s => s.Definition.Difficulty).ToArray(), $"day {day}");
                Assert.AreEqual(3, active.SelectMany(s => s.Definition.Categories).Distinct().Count(), $"day {day}");

                _now = _now.AddDays(1);
            }
        }

        [Test]
        public async Task Restart_QuestRemovedFromConfig_RefillKeepsRules()
        {
            // a1 and b2 are the only quests for their slots; the hard slot gets c3 or d3
            // (a3 conflicts with a1 by category).
            var pool = new[] { Ranked("a1", 1, "a"), Ranked("b2", 2, "b"), Ranked("c3", 3, "c"), Ranked("d3", 3, "d"), Ranked("a3", 3, "a") };
            _provider.Config = RankedConfig(pool);
            await Start();
            var hard = _service.GetActive().Last().Id.Value;

            _provider.Config = RankedConfig(pool.Where(q => q.Id.Value != hard).ToArray());
            await Start();

            var expected = new[] { "a1", "b2", hard == "c3" ? "d3" : "c3" };
            CollectionAssert.AreEqual(expected, _service.GetActive().Select(s => s.Id.Value).ToArray());
            Assert.AreEqual(1, _bridge.Rotations.Count, "a refill is not a new set");
        }
    }
}
