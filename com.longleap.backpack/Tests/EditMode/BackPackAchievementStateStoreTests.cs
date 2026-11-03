using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Achievements;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static LongLeap.Backpack.Tests.BackPackTestData;

namespace LongLeap.Backpack.Tests
{
    [TestFixture]
    public class BackPackAchievementStateStoreTests
    {
        private static readonly AchievementId Tutorial = new AchievementId("tutorial_completed");

        private sealed class RecordingStore : IAchievementStateStore
        {
            public readonly List<AchievementId> Marked = new List<AchievementId>();
            public readonly AchievementStatesSnapshot Snapshot = new AchievementStatesSnapshot();
            public int Deletes;

            public Task<AchievementStatesSnapshot> LoadAsync(AchievementDefinition[] definitions) =>
                Task.FromResult(Snapshot);

            public Task MarkUnlockedAsync(AchievementId id, long timestamp)
            {
                Marked.Add(id);
                return Task.CompletedTask;
            }

            public Task DeleteAllAsync()
            {
                Deletes++;
                return Task.CompletedTask;
            }
        }

        [Test]
        public async Task Accepted_SendsTheAchievementIdAsEvent_ThenPersists()
        {
            var inner = new RecordingStore();
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Ok("{\"success\":true,\"rewardGranted\":false}"));

            await new BackPackAchievementStateStore(inner, Service(transport)).MarkUnlockedAsync(Tutorial, 1);

            Assert.AreEqual(1, transport.Calls.Count);
            Assert.AreEqual(Tutorial.Value, (string)JObject.Parse(transport.LastCall.Body)["event"]);
            CollectionAssert.AreEqual(new[] { Tutorial }, inner.Marked);
        }

        // Not persisted: next session the unlock happens again and the event is re-sent
        // with the same eventId, so the server will not grant the reward twice.
        [Test]
        public async Task Rejected_DoesNotPersist()
        {
            var inner = new RecordingStore();
            var transport = new FakeBackPackTransport(BackPackHttpReply.Transport("no network"));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("tutorial_completed"));
            await new BackPackAchievementStateStore(inner, Service(transport)).MarkUnlockedAsync(Tutorial, 1);

            Assert.AreEqual(0, inner.Marked.Count);
        }

        [Test]
        public async Task Guest_PersistsLocally_WithoutSending()
        {
            var inner = new RecordingStore();
            var transport = new FakeBackPackTransport();

            await new BackPackAchievementStateStore(inner, Service(transport, xsollaId: null))
                .MarkUnlockedAsync(Tutorial, 1);

            Assert.AreEqual(0, transport.Calls.Count);
            CollectionAssert.AreEqual(new[] { Tutorial }, inner.Marked);
        }

        [Test]
        public async Task BackPackOff_PersistsLocally()
        {
            var inner = new RecordingStore();

            await new BackPackAchievementStateStore(inner, NullBackPackService.Instance).MarkUnlockedAsync(Tutorial, 1);
            await new BackPackAchievementStateStore(inner, null).MarkUnlockedAsync(Tutorial, 2);

            Assert.AreEqual(2, inner.Marked.Count);
        }

        [Test]
        public async Task LoadAndDelete_AreDelegated()
        {
            var inner = new RecordingStore();
            var store = new BackPackAchievementStateStore(inner, NullBackPackService.Instance);

            Assert.AreSame(inner.Snapshot, await store.LoadAsync(new AchievementDefinition[0]));
            await store.DeleteAllAsync();

            Assert.AreEqual(1, inner.Deletes);
        }
    }
}
