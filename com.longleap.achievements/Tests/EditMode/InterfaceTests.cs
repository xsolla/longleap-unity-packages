using LongLeap.Achievements;
using LongLeap.Stats;
using NUnit.Framework;
using System.Threading.Tasks;

#nullable enable

namespace LongLeap.Achievements.Tests
{
    [TestFixture]
    public class IAchievementSystemInterfaceTests
    {
        private sealed class MockProvider : IAchievementProvider
        {
            public Task<AchievementDefinition[]> LoadAsync() => Task.FromResult(System.Array.Empty<AchievementDefinition>());
        }

        private sealed class MockStateStore : IAchievementStateStore
        {
            public Task<AchievementStatesSnapshot> LoadAsync(AchievementDefinition[] definitions) => Task.FromResult(new AchievementStatesSnapshot());
            public Task MarkUnlockedAsync(AchievementId id, long timestamp) => Task.CompletedTask;
            public Task DeleteAllAsync() => Task.CompletedTask;
        }

        private sealed class MockGameBridge : IAchievementGameBridge
        {
            public void OnAvailable(AchievementId id) { }
            public void OnUnlocked(AchievementDefinition definition) { }
        }

        private sealed class MockSystem : IAchievementSystem
        {
            public Task Initialize(IAchievementProvider provider, IAchievementStateStore stateStore, IAchievementGameBridge? gameBridge = null, IStatSystem? stats = null) => Task.CompletedTask;
            public void Shutdown() { }
            public void ForceUnlock(AchievementId achievementId) { }
            public void ResetAll() { }
            public void Save() { }
            public bool IsDirty => false;
            public AchievementStatus[] GetAll() => System.Array.Empty<AchievementStatus>();
            public AchievementStatus[] GetByGroup(GroupId groupId) => System.Array.Empty<AchievementStatus>();
            public AchievementStatus[] GetAvailable() => System.Array.Empty<AchievementStatus>();
            public AchievementStatus[] GetAchieved() => System.Array.Empty<AchievementStatus>();
            public AchievementStatus[] GetPartiallyAchieved() => System.Array.Empty<AchievementStatus>();
            public AchievementStatus? GetStatus(AchievementId achievementId) => null;
            public GroupId[] GetGroups() => System.Array.Empty<GroupId>();
        }

        [Test]
        public void MockSystem_ImplementsInterface()
        {
            IAchievementSystem system = new MockSystem();
            Assert.NotNull(system);
        }

        [Test]
        public void MockProvider_ImplementsInterface()
        {
            IAchievementProvider provider = new MockProvider();
            Assert.NotNull(provider);
        }

        [Test]
        public void MockStateStore_ImplementsInterface()
        {
            IAchievementStateStore store = new MockStateStore();
            Assert.NotNull(store);
        }

        [Test]
        public void MockGameBridge_ImplementsInterface()
        {
            IAchievementGameBridge bridge = new MockGameBridge();
            Assert.NotNull(bridge);
        }

        [Test]
        public void MockSystem_InitializeAcceptsIStatSystem()
        {
            var system = new MockSystem();
            Assert.NotNull(system);
        }

        [Test]
        public void MockSystem_GetStatus_ReturnsNullable()
        {
            var system = new MockSystem();
            AchievementStatus? result = system.GetStatus(new AchievementId("nonexistent"));
            Assert.IsNull(result);
        }
    }
}
