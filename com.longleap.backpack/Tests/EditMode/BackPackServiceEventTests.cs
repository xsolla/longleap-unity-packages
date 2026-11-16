using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LongLeap.Backpack.Tests.BackPackTestData;

namespace LongLeap.Backpack.Tests
{
    [TestFixture]
    public class BackPackServiceEventTests
    {
        [Test]
        public async Task SendEvent_PostsBodyWithStableEventId_AndParsesReward()
        {
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Ok("{\"success\":true,\"rewardGranted\":true,\"rewardId\":\"r1\"}"));

            BackPackReply reply = await Service(transport).SendEventAsync("tutorial_completed");

            Assert.IsTrue(reply.Success);
            Assert.IsTrue(reply.RewardGranted);
            Assert.AreEqual("r1", reply.RewardId);

            BackPackHttpCall call = transport.LastCall;
            Assert.AreEqual("POST", call.Method);
            Assert.AreEqual(BaseUrl + "/events", call.Url, "trailing slash of baseUrl is trimmed");
            Assert.AreEqual(10, call.TimeoutSeconds);

            var body = JObject.Parse(call.Body);
            Assert.AreEqual(GameId, (string)body["gameId"]);
            Assert.AreEqual(XsollaId, (string)body["xsollaId"]);
            Assert.AreEqual("tutorial_completed", (string)body["event"]);
            Assert.AreEqual(
                BackPackService.StableEventId(GameId, XsollaId, "tutorial_completed"), (string)body["eventId"]);
        }

        [Test]
        public async Task SendEvent_UsesTheGivenEventId()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("{\"success\":true}"));

            await Service(transport).SendEventAsync("level_completed", "run-42");

            Assert.AreEqual("run-42", (string)JObject.Parse(transport.LastCall.Body)["eventId"]);
        }

        // "No reward for this event" is a successful answer: the achievement must be saved.
        [Test]
        public async Task SendEvent_SuccessWithoutReward_IsSuccess()
        {
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Ok("{\"success\":true,\"rewardGranted\":false}"));

            BackPackReply reply = await Service(transport).SendEventAsync("e");

            Assert.IsTrue(reply.Success);
            Assert.IsFalse(reply.RewardGranted);
            Assert.IsNull(reply.RewardId);
        }

        [Test]
        public async Task SendEvent_Rejected_ReturnsTheServerErrorCode()
        {
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Status(400, "{\"success\":false,\"error\":\"invalid_client\"}"));

            BackPackReply reply = await Service(transport).SendEventAsync("e");

            Assert.IsFalse(reply.Success);
            Assert.AreEqual("invalid_client", reply.Error);
        }

        [Test]
        public async Task SendEvent_SuccessBodyWithErrorStatus_IsNotSuccess()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Status(500, "{\"success\":true}"));

            BackPackReply reply = await Service(transport).SendEventAsync("e");

            Assert.IsFalse(reply.Success);
            Assert.AreEqual("http_500_with_success_body", reply.Error);
        }

        [Test]
        public async Task SendEvent_EmptyBody_Fails()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok(""));

            BackPackReply reply = await Service(transport).SendEventAsync("e");

            Assert.AreEqual("http_200_empty_body", reply.Error);
        }

        [Test]
        public async Task SendEvent_BadJson_Fails()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("<html>not json</html>"));

            BackPackReply reply = await Service(transport).SendEventAsync("e");

            Assert.IsFalse(reply.Success);
            StringAssert.StartsWith("bad_json", reply.Error);
        }

        [Test]
        public async Task SendEvent_TransportError_Fails()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Transport("no network"));

            BackPackReply reply = await Service(transport).SendEventAsync("e");

            Assert.AreEqual("transport: no network", reply.Error);
        }

        [Test]
        public async Task SendEvent_TransportThrows_FailsInsteadOfThrowing()
        {
            var transport = new FakeBackPackTransport { Throw = new InvalidOperationException("kaboom") };

            BackPackReply reply = await Service(transport).SendEventAsync("e");

            Assert.IsFalse(reply.Success);
            StringAssert.StartsWith("transport: POST " + BaseUrl + "/events", reply.Error);
            StringAssert.Contains("kaboom", reply.Error);
        }

        [Test]
        public async Task SendEvent_EmptyName_FailsWithoutRequest()
        {
            var transport = new FakeBackPackTransport();

            BackPackReply reply = await Service(transport).SendEventAsync("");

            Assert.AreEqual("empty_event_name", reply.Error);
            Assert.AreEqual(0, transport.Calls.Count);
        }

        [Test]
        public async Task SendEvent_Guest_FailsWithoutRequest()
        {
            var transport = new FakeBackPackTransport();
            var service = Service(transport, xsollaId: null);

            BackPackReply reply = await service.SendEventAsync("e");

            Assert.IsFalse(service.HasXsollaId);
            Assert.AreEqual("no_xsolla_id", reply.Error);
            Assert.AreEqual(0, transport.Calls.Count);
        }

        [TestCase(null, null)]
        [TestCase("", GameId)]
        [TestCase(BaseUrl, "")]
        [TestCase("   ", GameId)]
        public async Task SendEvent_NotConfigured_FailsWithoutRequest(string baseUrl, string gameId)
        {
            var transport = new FakeBackPackTransport();
            BackPackEndpoint endpoint = baseUrl == null && gameId == null ? null : new BackPackEndpoint(baseUrl, gameId);
            var service = new BackPackService(() => XsollaId, endpoint, transport);

            BackPackReply reply = await service.SendEventAsync("e");

            Assert.IsFalse(service.IsAvailable);
            Assert.AreEqual("not_configured", reply.Error);
            Assert.AreEqual(0, transport.Calls.Count);
        }

        // The id is read on every request: sign-in after the service was built takes effect.
        [Test]
        public async Task SendEvent_ReadsTheXsollaIdOnEveryRequest()
        {
            string current = null;
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("{\"success\":true}"));
            var service = new BackPackService(() => current, Endpoint(), transport);

            Assert.AreEqual("no_xsolla_id", (await service.SendEventAsync("e")).Error);

            current = "late-player";
            Assert.IsTrue((await service.SendEventAsync("e")).Success);
            Assert.AreEqual("late-player", (string)JObject.Parse(transport.LastCall.Body)["xsollaId"]);
        }
    }
}
