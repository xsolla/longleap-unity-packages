using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using static LongLeap.Backpack.Tests.BackPackTestData;

namespace LongLeap.Backpack.Tests
{
    // Cancellation by the caller and Dispose of the service.
    [TestFixture]
    public class BackPackServiceLifetimeTests
    {
        // Awaited rather than Assert.CatchAsync: the continuation is posted to Unity's
        // synchronization context, and a blocking wait on it would never finish.
        private static async Task<OperationCanceledException> CatchCancellation(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException exception)
            {
                return exception;
            }

            return null;
        }

        // The only exception the service lets out: the caller asked for it.
        [Test]
        public async Task CallerCancellation_Throws()
        {
            var transport = new FakeBackPackTransport { Hang = true };
            var cancellation = new CancellationTokenSource();

            Task<BackPackRewardsReply> pending = Service(transport).GetRewardsAsync(cancellation.Token);
            cancellation.Cancel();

            Assert.IsNotNull(await CatchCancellation(pending));
        }

        [Test]
        public async Task AlreadyCancelledToken_Throws()
        {
            var transport = new FakeBackPackTransport { Hang = true };

            Assert.IsNotNull(await CatchCancellation(
                Service(transport).SendEventAsync("e", cancellationToken: new CancellationToken(true))));
        }

        // Dispose on quit must not throw into AchievementSystem: the in-flight event fails instead,
        // so the unlock stays unsaved and is retried next session.
        [Test]
        public async Task Dispose_AbortsTheRequestInFlight_WithoutThrowing()
        {
            var transport = new FakeBackPackTransport { Hang = true };
            var service = Service(transport);

            Task<BackPackReply> pending = service.SendEventAsync("e");
            service.Dispose();
            BackPackReply reply = await pending;

            Assert.IsFalse(reply.Success);
            StringAssert.StartsWith("transport:", reply.Error);
            StringAssert.Contains("disposed", reply.Error);
        }

        [Test]
        public async Task AfterDispose_NothingIsSent_ButTheServiceStillLooksAvailable()
        {
            var transport = new FakeBackPackTransport();
            var service = Service(transport);

            service.Dispose();

            Assert.AreEqual("disposed", (await service.SendEventAsync("e")).Error);
            Assert.AreEqual("disposed", (await service.GetRewardsAsync()).Error);
            Assert.AreEqual("disposed", (await service.DeleteItemAsync("i1")).Error);
            Assert.AreEqual("disposed", (await service.CheckConnectionAsync()).Error);
            Assert.AreEqual(0, transport.Calls.Count);

            // Otherwise BackPackAchievementStateStore would save the unlock without its event.
            Assert.IsTrue(service.IsAvailable);
        }

        [Test]
        public void Dispose_Twice_IsHarmless()
        {
            var service = Service(new FakeBackPackTransport());

            service.Dispose();
            Assert.DoesNotThrow(service.Dispose);
        }

        [Test]
        public async Task UnityWebRequestTransport_CancelledToken_ThrowsBeforeSending()
        {
            var transport = new UnityWebRequestBackPackTransport();
            var call = new BackPackHttpCall(BackPackHttpCall.Get, BaseUrl + "/rewards", null, 1);

            Assert.IsNotNull(await CatchCancellation(transport.SendAsync(call, new CancellationToken(true))));
        }
    }
}
