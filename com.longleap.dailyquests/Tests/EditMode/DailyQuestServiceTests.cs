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
            public Task<DailyQuestsState> LoadAsync() => Task.FromResult<DailyQuestsState>(null);
            public Task SaveAsync(DailyQuestsState state) => Task.CompletedTask;
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

        // --- выдача ---------------------------------------------------------

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

        // --- прогресс -------------------------------------------------------

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
            Assert.AreEqual(0, _bridge.Claims.Count, "награда не должна начисляться сама");
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
        public async Task StatChange_RaisesChanged()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            int raised = 0;
            _service.Changed += () => raised++;

            _stats.Add(Grind, 1);

            Assert.AreEqual(1, raised);
        }

        // --- награда ----------------------------------------------------------

        [Test]
        public async Task Claim_CompletedQuest_AppliesRewardsOnce()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 50);

            Assert.IsTrue(_service.Claim(new QuestId("grind_50")));
            Assert.IsFalse(_service.Claim(new QuestId("grind_50")));

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

            Assert.IsFalse(_service.Claim(new QuestId("grind_50")));
            Assert.AreEqual(0, _bridge.Claims.Count);
        }

        [Test]
        public async Task Claim_QuestNotActive_ReturnsFalse()
        {
            await Start();

            Assert.IsFalse(_service.Claim(new QuestId("nope")));
            Assert.IsFalse(_service.Claim(QuestId.None));
        }

        [Test]
        public async Task ForceComplete_MakesQuestClaimable()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();

            _service.ForceComplete(new QuestId("grind_50"));

            Assert.IsTrue(_service.Claim(new QuestId("grind_50")));
        }

        // --- сохранение -------------------------------------------------------

        [Test]
        public async Task Restart_SamePeriod_KeepsSetAndBaseline()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            _stats.Set(Grind, 100);
            await Start();
            _stats.Add(Grind, 20);

            await Start();

            Assert.AreEqual(new QuestProgress(20, 50), Only().TotalProgress);
            Assert.AreEqual(1, _bridge.Rotations.Count, "перезапуск в тот же день не выдаёт новый набор");
        }

        [Test]
        public async Task Restart_ClaimedQuest_StaysClaimed()
        {
            _provider.Config = Config(1, Quest("grind_50", "grind", 50, 5));
            await Start();
            _stats.Add(Grind, 50);
            _service.Claim(new QuestId("grind_50"));

            await Start();

            Assert.AreEqual(QuestState.Claimed, Only().State);
            Assert.IsFalse(_service.Claim(new QuestId("grind_50")));
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
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("состояние не прочиталось"));

            await Start(new UnavailableStore());

            Assert.IsEmpty(_service.GetActive());
            Assert.IsFalse(_service.Claim(new QuestId("grind_50")));
        }

        // --- смена периода ------------------------------------------------------

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
            Assert.IsFalse(_service.Claim(new QuestId("grind_50")));
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

            _now = _now.AddHours(5); // 17:00 — тот же период
            _service.GetActive();
            Assert.AreEqual(1, _bridge.Rotations.Count);

            _now = _now.AddHours(1); // 18:00 — новый
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

            _service.DebugSkipToNextResetWindow();

            Assert.AreEqual(2, _bridge.Rotations.Count);
        }
    }
}
