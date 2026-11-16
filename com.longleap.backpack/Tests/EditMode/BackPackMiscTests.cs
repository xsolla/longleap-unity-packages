using System;
using System.Threading.Tasks;
using NUnit.Framework;
using static LongLeap.Backpack.Tests.BackPackTestData;

namespace LongLeap.Backpack.Tests
{
    [TestFixture]
    public class StableEventIdTests
    {
        [Test]
        public void IsDeterministic()
        {
            Assert.AreEqual(
                BackPackService.StableEventId(GameId, XsollaId, "e"),
                BackPackService.StableEventId(GameId, XsollaId, "e"));
        }

        [Test]
        public void DiffersWhenAnyPartDiffers()
        {
            string id = BackPackService.StableEventId(GameId, XsollaId, "e");

            Assert.AreNotEqual(id, BackPackService.StableEventId("game_02", XsollaId, "e"));
            Assert.AreNotEqual(id, BackPackService.StableEventId(GameId, "player-2", "e"));
            Assert.AreNotEqual(id, BackPackService.StableEventId(GameId, XsollaId, "f"));
        }

        // The server expects a uuid: version 3 (name-based), RFC 4122 variant.
        [Test]
        public void IsAVersion3Uuid()
        {
            string id = BackPackService.StableEventId(GameId, XsollaId, "e");

            Assert.IsTrue(Guid.TryParse(id, out _), id);
            Assert.AreEqual('3', id[14], id);
            StringAssert.Contains(id[19].ToString(), "89ab", id);
        }
    }

    [TestFixture]
    public class BackPackHttpReplyTests
    {
        [Test]
        public void NotSent_IsNeitherTransportErrorNorSuccess()
        {
            BackPackHttpReply reply = BackPackHttpReply.NotSent("no_xsolla_id");

            Assert.IsTrue(reply.IsNotSent);
            Assert.IsFalse(reply.IsTransportError);
            Assert.IsFalse(reply.IsHttpSuccess);
            Assert.AreEqual("not sent: no_xsolla_id", reply.ToString());
        }

        [Test]
        public void Status_SuccessOnlyFor2xx()
        {
            Assert.IsTrue(BackPackHttpReply.Status(204).IsHttpSuccess);
            Assert.IsFalse(BackPackHttpReply.Status(301).IsHttpSuccess);
            Assert.IsFalse(BackPackHttpReply.Transport("x").IsHttpSuccess);
        }
    }

    [TestFixture]
    public class BackPackEndpointTests
    {
        // Paths are normalized (an empty or slash-less path is a typo); baseUrl and gameId are only trimmed
        // (empty is the "off" switch, not a reason to substitute a default).
        [Test]
        public void TrimsValues_AndNormalizesPaths()
        {
            var endpoint = new BackPackEndpoint(" http://h:1 ", " g ", "", "rewards", null);

            Assert.AreEqual("http://h:1", endpoint.BaseUrl);
            Assert.AreEqual("g", endpoint.GameId);
            Assert.AreEqual(BackPackEndpoint.DefaultEventsPath, endpoint.EventsPath);
            Assert.AreEqual("/rewards", endpoint.RewardsPath);
            Assert.AreEqual(BackPackEndpoint.DefaultItemsPath, endpoint.ItemsPath);
            Assert.IsTrue(endpoint.IsConfigured);
        }

        [Test]
        public void EmptyBaseUrl_IsNotConfigured()
        {
            Assert.IsFalse(new BackPackEndpoint("", GameId).IsConfigured);
        }
    }

    [TestFixture]
    public class NullBackPackServiceTests
    {
        [Test]
        public async Task AnswersNotConfiguredToEverything()
        {
            IBackPackService service = NullBackPackService.Instance;

            Assert.IsFalse(service.IsAvailable);
            Assert.IsFalse(service.HasXsollaId);
            Assert.IsNull(service.XsollaId);
            Assert.AreEqual("not_configured", (await service.SendEventAsync("e")).Error);
            Assert.AreEqual("not_configured", (await service.GetRewardsAsync()).Error);
            Assert.AreEqual("not_configured", (await service.DeleteItemAsync("i1")).Error);
            Assert.IsFalse((await service.CheckConnectionAsync()).IsConnected);
        }
    }

    [TestFixture]
    public class BackPackDebugApiTests
    {
        private static BackPackDebugApi Api(FakeBackPackTransport transport, string xsollaId = XsollaId) =>
            new BackPackDebugApi(() => xsollaId, Endpoint(), transport);

        [Test]
        public async Task DeleteClaims_AlwaysScopesToOurGame()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("{\"deleted\":[]}"));

            BackPackHttpReply reply = await Api(transport).DeleteClaimsAsync(eventId: "e1");

            Assert.IsTrue(reply.IsHttpSuccess);
            Assert.AreEqual("DELETE", transport.LastCall.Method);
            Assert.AreEqual(
                BaseUrl + "/events?xsollaId=" + XsollaId + "&gameId=" + GameId + "&eventId=e1&backpack=true&force=false",
                transport.LastCall.Url);
        }

        [Test]
        public async Task DeleteBackpackItems_WithoutFilters_TargetsTheWholeBackpack()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("{\"items\":[]}"));

            await Api(transport).DeleteBackpackItemsAsync();

            Assert.AreEqual(BaseUrl + "/backpack/items?xsollaId=" + XsollaId, transport.LastCall.Url);
        }

        [Test]
        public async Task Guest_IsNotSent_RatherThanATransportError()
        {
            var transport = new FakeBackPackTransport();

            BackPackHttpReply items = await Api(transport, xsollaId: null).DeleteBackpackItemsAsync();
            BackPackHttpReply claims = await Api(transport, xsollaId: null).DeleteClaimsAsync();

            Assert.IsTrue(items.IsNotSent);
            Assert.IsFalse(items.IsTransportError);
            Assert.AreEqual("no_xsolla_id", items.NotSentReason);
            Assert.AreEqual("no_xsolla_id", claims.NotSentReason);
            Assert.AreEqual(0, transport.Calls.Count);
        }
    }
}
