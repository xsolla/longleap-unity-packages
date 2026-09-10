using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.Achievements;
using LongLeap.Stats;
using LongLeap.Stats.Tests;
using NUnit.Framework;

namespace LongLeap.Achievements.Tests
{
    [TestFixture]
    public class AchievementSystemTests
    {
        private sealed class SimpleStore : IAchievementStateStore
        {
            public AchievementStatesSnapshot Snapshot { get; set; } = new AchievementStatesSnapshot
            {
                Version = 1,
                Entries = Array.Empty<AchievementStateEntry>()
            };

            public List<(AchievementId, long)> MarkUnlockedCalls { get; } = new();
            public int SaveCallCount => MarkUnlockedCalls.Count;
            public void ResetCallCounts() => MarkUnlockedCalls.Clear();

            public Task<AchievementStatesSnapshot> LoadAsync(AchievementDefinition[] definitions)
                => Task.FromResult(Snapshot);

            public async Task MarkUnlockedAsync(AchievementId id, long timestamp)
            {
                MarkUnlockedCalls.Add((id, timestamp));
                var entry = FindOrCreateEntry(id.Value);
                entry.IsUnlocked = true;
                entry.UnlockedTimestamp = timestamp;
                await Task.CompletedTask;
            }

            public async Task DeleteAllAsync()
            {
                Snapshot = new AchievementStatesSnapshot { Version = 1, Entries = Array.Empty<AchievementStateEntry>() };
                await Task.CompletedTask;
            }

            private AchievementStateEntry FindOrCreateEntry(string achievementId)
            {
                var entry = Snapshot.Entries?.FirstOrDefault(e => e.AchievementId == achievementId);
                if (entry == null)
                {
                    entry = new AchievementStateEntry { AchievementId = achievementId };
                    var list = Snapshot.Entries?.ToList() ?? new List<AchievementStateEntry>();
                    list.Add(entry);
                    Snapshot.Entries = list.ToArray();
                }
                return entry;
            }
        }

        private sealed class SimpleProvider : IAchievementProvider
        {
            public AchievementDefinition[] Definitions { get; set; } = Array.Empty<AchievementDefinition>();
            public Task<AchievementDefinition[]> LoadAsync() => Task.FromResult(Definitions);
        }

        private sealed class SpyBridge : IAchievementGameBridge
        {
            public List<AchievementId> AvailableCalls { get; } = new();
            public List<AchievementDefinition> UnlockedCalls { get; } = new();
            public void OnAvailable(AchievementId id) => AvailableCalls.Add(id);
            public void OnUnlocked(AchievementDefinition def) => UnlockedCalls.Add(def);
        }

        private static Objective StatObj(string id, string statPath, int target) =>
            new Objective { Id = new ObjectiveId(id), StatPath = statPath, StatTarget = target };

        private static Objective ReqObj(string id, params AchievementId[] required) =>
            new Objective { Id = new ObjectiveId(id), Required = required };

        private static AchievementDefinition Def(string id, string group, params Objective[] objectives) =>
            new AchievementDefinition { Id = new AchievementId(id), Group = group, Objectives = objectives };

        private static AchievementDefinition Def(string id, string group, AchievementId[] prereqs, params Objective[] objectives) =>
            new AchievementDefinition { Id = new AchievementId(id), Group = group, Prerequisites = prereqs, Objectives = objectives };

        private FakeStatSystem _stats;
        private AchievementSystem _system;
        private SimpleProvider _provider;
        private SimpleStore _store;
        private SpyBridge _bridge;

        [SetUp]
        public void Setup()
        {
            _stats = new FakeStatSystem();
            _system = new AchievementSystem();
            _provider = new SimpleProvider();
            _store = new SimpleStore();
            _bridge = new SpyBridge();
        }

        [TearDown]
        public void Teardown() => _system.Shutdown();

        [Test]
        public void Initialize_NullProvider_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() =>
                _system.Initialize(null, _store, _bridge, _stats));
        }

        [Test]
        public void Initialize_NullStore_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() =>
                _system.Initialize(_provider, null, _bridge, _stats));
        }

        [Test]
        public void Initialize_WithoutBridge_DoesNotThrow()
        {
            Assert.DoesNotThrowAsync(() =>
                _system.Initialize(_provider, _store, null, _stats));
        }

        [Test]
        public void Initialize_WithoutStats_DoesNotThrow()
        {
            Assert.DoesNotThrowAsync(() =>
                _system.Initialize(_provider, _store, _bridge));
        }

        [Test]
        public async Task StatObjective_ReachesTarget_UnlocksAchievement()
        {
            _provider.Definitions = new[]
            {
                Def("ach_kill", "combat", StatObj("obj_kill", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _stats.SetValue("kill:monk:grunt", 10);

            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == new AchievementId("ach_kill")));
            var status = _system.GetStatus(new AchievementId("ach_kill"));
            Assert.IsTrue(status.IsUnlocked);
        }

        [Test]
        public async Task StatObjective_BelowTarget_DoesNotUnlock()
        {
            _provider.Definitions = new[]
            {
                Def("ach_kill", "combat", StatObj("obj_kill", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _stats.SetValue("kill:monk:grunt", 5);

            Assert.IsFalse(_bridge.UnlockedCalls.Any(d => d.Id == new AchievementId("ach_kill")));
            var status = _system.GetStatus(new AchievementId("ach_kill"));
            Assert.IsFalse(status.IsUnlocked);
        }

        [Test]
        public async Task PrerequisiteAchievementUnlocks_DependentBecomesAvailable()
        {
            var achA = new AchievementId("ach_a");
            var achB = new AchievementId("ach_b");

            _provider.Definitions = new[]
            {
                Def("ach_a", "combat", StatObj("obj_a", "kill", 1)),
                Def("ach_b", "combat", new[] { achA })
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _stats.SetValue("kill:monk:grunt", 1);

            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == achA));
            Assert.IsTrue(_bridge.AvailableCalls.Contains(achB));
            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == achB));
        }

        [Test]
        public async Task ForceUnlock_TriggersPrerequisiteChain()
        {
            var achA = new AchievementId("ach_a");
            var achB = new AchievementId("ach_b");

            _provider.Definitions = new[]
            {
                Def("ach_a", "test", StatObj("obj_a", "kill", 10)),
                Def("ach_b", "test", new[] { achA })
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _system.ForceUnlock(achA);

            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == achA));
            Assert.IsTrue(_bridge.AvailableCalls.Contains(achB));
            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == achB));
        }

        [Test]
        public async Task RequiredObjective_AllMet_Unlocks()
        {
            var heroA = new AchievementId("hero_a");
            var all = new AchievementId("all_heroes");

            _provider.Definitions = new[]
            {
                new AchievementDefinition
                {
                    Id = heroA, Group = "heroes", IsHidden = true,
                    Objectives = new[] { new Objective { Id = new ObjectiveId("obj_a"), StatPath = "game_started:hero_a", StatTarget = 1 } }
                },
                new AchievementDefinition
                {
                    Id = all, Group = "heroes",
                    Objectives = new[] { new Objective { Id = new ObjectiveId("obj_all"), Required = new[] { heroA } } }
                }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _stats.SetValue("game_started:hero_a", 1);

            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == heroA));
            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == all));
        }

        [Test]
        public async Task RequiredObjective_NotAllMet_DoesNotUnlock()
        {
            var heroA = new AchievementId("hero_a");
            var heroB = new AchievementId("hero_b");
            var all = new AchievementId("all_heroes");

            _provider.Definitions = new[]
            {
                new AchievementDefinition
                {
                    Id = heroA, Group = "heroes", IsHidden = true,
                    Objectives = new[] { new Objective { Id = new ObjectiveId("obj_a"), StatPath = "game_started:hero_a", StatTarget = 1 } }
                },
                new AchievementDefinition
                {
                    Id = heroB, Group = "heroes", IsHidden = true,
                    Objectives = new[] { new Objective { Id = new ObjectiveId("obj_b"), StatPath = "game_started:hero_b", StatTarget = 1 } }
                },
                new AchievementDefinition
                {
                    Id = all, Group = "heroes",
                    Objectives = new[] { new Objective { Id = new ObjectiveId("obj_all"), Required = new[] { heroA, heroB } } }
                }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _stats.SetValue("game_started:hero_a", 1);

            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == heroA));
            Assert.IsFalse(_bridge.UnlockedCalls.Any(d => d.Id == all));
        }

        [Test]
        public async Task HiddenAchievement_ExcludedFromGetAvailable()
        {
            _provider.Definitions = new[]
            {
                Def("visible", "test", StatObj("o", "xp", 1)),
                new AchievementDefinition { Id = new AchievementId("hidden"), Group = "test", IsHidden = true, Objectives = new[] { new Objective { Id = new ObjectiveId("o2"), StatPath = "level", StatTarget = 1 } } }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            var available = _system.GetAvailable();
            Assert.AreEqual(1, available.Length);
            Assert.AreEqual("visible", available[0].Definition.Id.Value);
        }

        [Test]
        public async Task GetAchieved_ReturnsUnlockedAchievements()
        {
            _store.Snapshot = new AchievementStatesSnapshot
            {
                Version = 1,
                Entries = new[] { new AchievementStateEntry { AchievementId = "legacy", IsUnlocked = true } }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            var achieved = _system.GetAchieved();
            Assert.AreEqual(1, achieved.Length);
            Assert.AreEqual("legacy", achieved[0].Definition.Id.Value);
        }

        [Test]
        public async Task GetAll_IncludesLegacyAndHidden()
        {
            _provider.Definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("visible"), Group = "test", Objectives = Array.Empty<Objective>() },
                new AchievementDefinition { Id = new AchievementId("hidden"), Group = "test", IsHidden = true, Objectives = Array.Empty<Objective>() }
            };

            _store.Snapshot = new AchievementStatesSnapshot
            {
                Version = 1,
                Entries = new[] { new AchievementStateEntry { AchievementId = "legacy", IsUnlocked = true } }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            var all = _system.GetAll();
            var ids = all.Select(s => s.Definition.Id.Value).OrderBy(x => x).ToArray();
            Assert.AreEqual(new[] { "hidden", "legacy", "visible" }, ids);
        }

        [Test]
        public async Task GetByGroup_FiltersCorrectly()
        {
            _provider.Definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("a"), Group = "combat", Objectives = Array.Empty<Objective>() },
                new AchievementDefinition { Id = new AchievementId("b"), Group = "exploration", Objectives = Array.Empty<Objective>() }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            var combat = _system.GetByGroup("combat");
            Assert.AreEqual(1, combat.Length);
            Assert.AreEqual("a", combat[0].Definition.Id.Value);
        }

        [Test]
        public async Task GetGroups_ReturnsDistinct()
        {
            _provider.Definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("a"), Group = "combat", Objectives = Array.Empty<Objective>() },
                new AchievementDefinition { Id = new AchievementId("b"), Group = "combat", Objectives = Array.Empty<Objective>() },
                new AchievementDefinition { Id = new AchievementId("c"), Group = "exploration", Objectives = Array.Empty<Objective>() }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            var groups = _system.GetGroups().Select(g => g.Value).OrderBy(x => x).ToArray();
            Assert.AreEqual(new[] { "combat", "exploration" }, groups);
        }

        [Test]
        public async Task GetStatus_ReturnsNull_ForUnknown()
        {
            await _system.Initialize(_provider, _store, _bridge, _stats);
            Assert.IsNull(_system.GetStatus(new AchievementId("nonexistent")));
        }

        [Test]
        public async Task GetStatus_ReturnsData_ForKnown()
        {
            _provider.Definitions = new[]
            {
                Def("test_ach", "test", StatObj("o", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            var status = _system.GetStatus(new AchievementId("test_ach"));
            Assert.NotNull(status);
            Assert.AreEqual("test_ach", status.Definition.Id.Value);
            Assert.IsTrue(status.IsAvailable);
            Assert.IsFalse(status.IsUnlocked);
        }

        [Test]
        public async Task ResetAll_ClearsAllState()
        {
            _provider.Definitions = new[]
            {
                Def("ach", "test", StatObj("o", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _system.ForceUnlock(new AchievementId("ach"));

            _system.ResetAll();

            var status = _system.GetStatus(new AchievementId("ach"));
            Assert.NotNull(status);
            Assert.IsFalse(status.IsUnlocked);
        }

        [Test]
        public async Task UnlockAchievement_DoesNotDuplicate()
        {
            var achId = new AchievementId("ach");
            _provider.Definitions = new[]
            {
                new AchievementDefinition { Id = achId, Group = "test", Objectives = Array.Empty<Objective>() }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _system.ForceUnlock(achId);
            _bridge.UnlockedCalls.Clear();

            _system.ForceUnlock(achId);

            Assert.AreEqual(0, _bridge.UnlockedCalls.Count);
        }

        [Test]
        public async Task UnlockAchievement_SavesImmediately()
        {
            var achId = new AchievementId("ach");
            _provider.Definitions = new[]
            {
                Def("ach", "test", StatObj("o", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _store.ResetCallCounts();

            _stats.SetValue("kill:monk:grunt", 10);

            Assert.IsTrue(_store.SaveCallCount >= 1);
        }

        [Test]
        public async Task Shutdown_WithNoChanges_DoesNotSave()
        {
            await _system.Initialize(_provider, _store, _bridge, _stats);
            _store.ResetCallCounts();

            _system.Shutdown();

            Assert.AreEqual(0, _store.SaveCallCount);
        }

        [Test]
        public async Task Shutdown_SavesChanges()
        {
            _provider.Definitions = new[]
            {
                Def("ach", "test", StatObj("o", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            _stats.SetValue("kill:monk:grunt", 10);

            _system.Shutdown();

            Assert.IsTrue(_store.SaveCallCount >= 1);
        }

        [Test]
        public async Task ZeroObjectives_AutoUnlocks()
        {
            var achId = new AchievementId("auto_ach");
            _provider.Definitions = new[]
            {
                new AchievementDefinition { Id = achId, Group = "test", Objectives = Array.Empty<Objective>() }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == achId));
            var status = _system.GetStatus(achId);
            Assert.IsTrue(status.IsUnlocked);
        }

        [Test]
        public async Task GetAvailable_ExcludesAlreadyUnlocked()
        {
            var achId = new AchievementId("ach");
            _provider.Definitions = new[]
            {
                new AchievementDefinition { Id = achId, Group = "test", Objectives = Array.Empty<Objective>() }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _system.ForceUnlock(achId);

            var available = _system.GetAvailable();
            Assert.IsEmpty(available);
        }

        [Test]
        public async Task GetPartiallyAchieved_ReturnsInProgress()
        {
            _provider.Definitions = new[]
            {
                Def("ach", "test", StatObj("o", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);
            _stats.SetValue("kill:monk:grunt", 5);

            var partial = _system.GetPartiallyAchieved();
            Assert.AreEqual(1, partial.Length);
            Assert.AreEqual("ach", partial[0].Definition.Id.Value);
        }

        [Test]
        public async Task PreviouslyUnlocked_RestoresFromSnapshot()
        {
            _store.Snapshot = new AchievementStatesSnapshot
            {
                Version = 1,
                Entries = new[] { new AchievementStateEntry { AchievementId = "old_ach", IsUnlocked = true } }
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            var achieved = _system.GetAchieved();
            Assert.AreEqual(1, achieved.Length);
        }

        [Test]
        public async Task StatObjective_InitialValueFromStats_IsCached()
        {
            _stats.SetAggregate("kill", 7);

            _provider.Definitions = new[]
            {
                Def("ach_kill", "combat", StatObj("o", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            var status = _system.GetStatus(new AchievementId("ach_kill"));
            Assert.AreEqual(7, status.ObjectiveProgresses[0].Progress.Current);
            Assert.AreEqual(10, status.ObjectiveProgresses[0].Progress.Target);
            Assert.IsFalse(status.IsUnlocked);
        }

        [Test]
        public async Task StatObjective_AlreadyAtTargetOnInit_Unlocks()
        {
            _stats.SetAggregate("kill", 15);

            _provider.Definitions = new[]
            {
                Def("ach_kill", "combat", StatObj("o", "kill", 10))
            };

            await _system.Initialize(_provider, _store, _bridge, _stats);

            Assert.IsTrue(_bridge.UnlockedCalls.Any(d => d.Id == new AchievementId("ach_kill")));
            var status = _system.GetStatus(new AchievementId("ach_kill"));
            Assert.IsTrue(status.IsUnlocked);
        }
    }
}
