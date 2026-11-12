using System;
using System.Threading.Tasks;
using NUnit.Framework;
using static LongLeap.Backpack.Tests.BackPackTestData;

namespace LongLeap.Backpack.Tests
{
    // GetRewardsAsync, DeleteItemAsync, CheckConnectionAsync.
    [TestFixture]
    public class BackPackServiceItemsTests
    {
        [Test]
        public async Task GetRewards_RequestsThePlayersBackpack_AndDropsNullItems()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok(
                "{\"xsollaId\":\"player-1\",\"gameId\":\"game_01\",\"items\":[" +
                "{\"id\":\"i1\",\"sku\":\"shield\",\"name\":\"Shield\"," +
                "\"customAttributes\":[{\"external_id\":\"reward\",\"values\":[{\"external_id\":\"shield_1\"}]}]},null]}"));

            BackPackRewardsReply reply = await Service(transport).GetRewardsAsync();

            Assert.AreEqual("GET", transport.LastCall.Method);
            Assert.AreEqual(BaseUrl + "/rewards?xsollaId=" + XsollaId + "&gameId=" + GameId, transport.LastCall.Url);
            Assert.IsNull(transport.LastCall.Body);

            Assert.IsTrue(reply.Success);
            Assert.AreEqual(1, reply.Items.Count);
            Assert.AreEqual("i1", reply.Items[0].Id);
            Assert.AreEqual("shield", reply.Items[0].Sku);
            CollectionAssert.AreEqual(new[] { "shield_1" }, reply.Items[0].CustomAttributes.ValueIds("reward"));
        }

        [Test]
        public async Task GetRewards_ParsesAttributesAsTheyAre()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok(
                "{\"xsollaId\":\"player-1\",\"gameId\":\"game_01\",\"items\":[" +
                "{\"id\":\"i1\",\"sku\":\"pack\",\"iconUrl\":\"https://x/i.png\"," +
                "\"createdAt\":\"2026-11-10T08:00:00Z\"," +
                "\"attributes\":[{\"external_id\":\"reward\",\"values\":[{\"external_id\":\"shield_1\"}]}]," +
                "\"customAttributes\":[" +
                "{\"name\":{\"en\":\"Reward\"},\"values\":[{\"value\":{\"en\":\"cash_10\"},\"external_id\":\"cash_10\"}],\"external_id\":\"reward\"}," +
                "{\"name\":{\"en\":\"Merge\"},\"values\":[{\"value\":{\"en\":\"first_climb\"},\"external_id\":\"first_climb\"}],\"external_id\":\"merge\"}]}," +
                "{\"id\":\"i2\",\"sku\":\"old\",\"rewards\":[\"cash_100\"]," +
                "\"attributes\":[{\"external_id\":\"reward\",\"values\":[{\"external_id\":\"shield_1\"}]}]}]}"));

            BackPackRewardsReply reply = await Service(transport).GetRewardsAsync();

            Assert.IsTrue(reply.Success);
            Assert.AreEqual("https://x/i.png", reply.Items[0].IconUrl);
            Assert.AreEqual("2026-11-10T08:00:00Z", reply.Items[0].CreatedAt);
            Assert.AreEqual(2, reply.Items[0].CustomAttributes.Count);
            CollectionAssert.AreEqual(new[] { "cash_10" }, reply.Items[0].CustomAttributes.ValueIds("reward"));
            Assert.AreEqual("first_climb", reply.Items[0].CustomAttributes.Find("merge").Values[0].ExternalId);
            Assert.AreEqual("shield_1", reply.Items[0].Attributes.Find("reward").Values[0].ExternalId);

            CollectionAssert.IsEmpty(reply.Items[1].CustomAttributes);
        }

        [Test]
        public async Task GetRewards_NoItemsField_IsAnEmptyBackpack()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("{}"));

            BackPackRewardsReply reply = await Service(transport).GetRewardsAsync();

            Assert.IsTrue(reply.Success);
            Assert.AreEqual(0, reply.Items.Count);
        }

        [Test]
        public async Task GetRewards_HttpError_KeepsStatusAndBody_AndItemsAreNeverNull()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Status(400, "invalid_xsolla_id"));

            BackPackRewardsReply reply = await Service(transport).GetRewardsAsync();

            Assert.IsFalse(reply.Success);
            Assert.AreEqual(400, reply.StatusCode);
            Assert.AreEqual("http_400: invalid_xsolla_id", reply.Error);
            Assert.IsNotNull(reply.Items);
            Assert.AreEqual(0, reply.Items.Count);
        }

        [Test]
        public async Task GetRewards_Guest_FailsWithoutRequest()
        {
            var transport = new FakeBackPackTransport();

            BackPackRewardsReply reply = await Service(transport, xsollaId: null).GetRewardsAsync();

            Assert.AreEqual("no_xsolla_id", reply.Error);
            Assert.AreEqual(0, reply.StatusCode);
            Assert.AreEqual(0, transport.Calls.Count);
        }

        [Test]
        public async Task DeleteItem_DeletesExactlyThatInstance()
        {
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Ok("{\"items\":[{\"itemId\":\"i1\",\"outcome\":\"item_deleted\"}]}"));

            BackPackDeleteResult result = await Service(transport).DeleteItemAsync("i1");

            Assert.IsTrue(result.IsDeleted);
            Assert.AreEqual("DELETE", transport.LastCall.Method);
            Assert.AreEqual(
                BaseUrl + "/backpack/items?xsollaId=" + XsollaId + "&itemId=i1", transport.LastCall.Url,
                "only by item id: deleting by sku would remove every item of that type");
        }

        // The server answers 200 for every outcome — only "item_deleted" for our item counts.
        [Test]
        public async Task DeleteItem_AlreadyGone_IsNotDeleted()
        {
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Ok("{\"items\":[{\"itemId\":\"i1\",\"outcome\":\"item_already_gone\"}]}"));

            BackPackDeleteResult result = await Service(transport).DeleteItemAsync("i1");

            Assert.IsFalse(result.IsDeleted);
            Assert.AreEqual(BackPackDeleteResult.OutcomeAlreadyGone, result.Outcome);
        }

        [Test]
        public async Task DeleteItem_BackpackFailed_ReportsTheItemsError()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok(
                "{\"items\":[{\"itemId\":\"i1\",\"outcome\":\"backpack_failed\",\"error\":\"upstream_502\"}]}"));

            BackPackDeleteResult result = await Service(transport).DeleteItemAsync("i1");

            Assert.IsFalse(result.IsDeleted);
            Assert.AreEqual("backpack_failed", result.Outcome);
            Assert.AreEqual("upstream_502", result.Error);
        }

        [Test]
        public async Task DeleteItem_ReportAboutAnotherItem_IsNotDeleted()
        {
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Ok("{\"items\":[{\"itemId\":\"other\",\"outcome\":\"item_deleted\"}]}"));

            BackPackDeleteResult result = await Service(transport).DeleteItemAsync("i1");

            Assert.IsFalse(result.IsDeleted);
            StringAssert.StartsWith("item_not_in_report", result.Error);
        }

        [Test]
        public async Task DeleteItem_HttpError_IsNotDeleted()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Status(502, "bad gateway"));

            BackPackDeleteResult result = await Service(transport).DeleteItemAsync("i1");

            Assert.IsFalse(result.IsDeleted);
            Assert.AreEqual("http_502: bad gateway", result.Error);
        }

        [Test]
        public async Task DeleteItem_NoItemId_FailsWithoutRequest()
        {
            var transport = new FakeBackPackTransport();

            BackPackDeleteResult result = await Service(transport).DeleteItemAsync(null);

            Assert.AreEqual("no_item_id", result.Error);
            Assert.AreEqual(0, transport.Calls.Count);
        }

        // A configuration problem is not a network problem: no "transport:" prefix.
        [Test]
        public async Task DeleteItem_NotConfiguredOrGuest_IsNotReportedAsTransportError()
        {
            var transport = new FakeBackPackTransport();

            BackPackDeleteResult notConfigured =
                await new BackPackService(() => XsollaId, null, transport).DeleteItemAsync("i1");
            BackPackDeleteResult guest = await Service(transport, xsollaId: null).DeleteItemAsync("i1");

            Assert.AreEqual("not_configured", notConfigured.Error);
            Assert.AreEqual("no_xsolla_id", guest.Error);
            Assert.AreEqual(0, transport.Calls.Count);
        }

        // Without xsollaId the server answers 400 — the fact of an answer is what matters.
        [Test]
        public async Task CheckConnection_BadRequest_MeansConnected_EvenForAGuest()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Status(400, "invalid_xsolla_id"));

            BackPackConnectionResult result = await Service(transport, xsollaId: null).CheckConnectionAsync();

            Assert.IsTrue(result.IsConnected);
            Assert.AreEqual(BaseUrl + "/rewards?gameId=" + GameId, transport.LastCall.Url);
            Assert.AreEqual(5, transport.LastCall.TimeoutSeconds, "the player waits on this check");
        }

        [Test]
        public async Task CheckConnection_NotFound_IsNotConnected()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Status(404, "{\"error\":\"unknown_game\"}"));

            BackPackConnectionResult result = await Service(transport).CheckConnectionAsync();

            Assert.IsFalse(result.IsConnected);
            Assert.AreEqual("http_404: {\"error\":\"unknown_game\"}", result.Error);
        }

        [Test]
        public async Task CheckConnection_ServerError_IsNotConnected()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Status(522));

            BackPackConnectionResult result = await Service(transport).CheckConnectionAsync();

            Assert.IsFalse(result.IsConnected);
            Assert.AreEqual("http_522", result.Error);
        }

        [Test]
        public async Task CheckConnection_TransportError_IsNotConnected()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Transport("timeout"));

            BackPackConnectionResult result = await Service(transport).CheckConnectionAsync();

            Assert.IsFalse(result.IsConnected);
            Assert.AreEqual("transport: timeout", result.Error);
        }

        [Test]
        public async Task CheckConnection_NotConfigured_IsNotConnected()
        {
            var transport = new FakeBackPackTransport();

            BackPackConnectionResult result =
                await new BackPackService(() => XsollaId, null, transport).CheckConnectionAsync();

            Assert.IsFalse(result.IsConnected);
            Assert.AreEqual("not_configured", result.Error);
            Assert.AreEqual(0, transport.Calls.Count);
        }

        [Test]
        public async Task GetRewards_SignedIn_SendsTheUserTokenHeader()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("{}"));
            var service = new BackPackService(() => XsollaId, Endpoint(), transport,
                userTokenProvider: _ => Task.FromResult("token-1"));

            await service.GetRewardsAsync();

            Assert.AreEqual("token-1", transport.LastCall.UserToken);
            StringAssert.DoesNotContain("token-1", transport.LastCall.ToString());
        }

        [Test]
        public async Task GetRewards_NoToken_SendsNoHeader()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("{}"));
            var service = new BackPackService(() => XsollaId, Endpoint(), transport,
                userTokenProvider: _ => Task.FromResult<string>(null));

            await service.GetRewardsAsync();

            Assert.IsNull(transport.LastCall.UserToken);
        }

        [Test]
        public async Task GetRewards_TokenLookupThrows_RequestStillGoesWithoutIt()
        {
            var transport = new FakeBackPackTransport(BackPackHttpReply.Ok("{}"));
            var service = new BackPackService(() => XsollaId, Endpoint(), transport,
                userTokenProvider: _ => throw new InvalidOperationException("refresh failed"));

            BackPackRewardsReply reply = await service.GetRewardsAsync();

            Assert.IsTrue(reply.Success);
            Assert.IsNull(transport.LastCall.UserToken);
        }

        [Test]
        public async Task GetRewards_TokenRefused_RefreshesAndRetriesOnce()
        {
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Status(401, "{\"error\":\"invalid_user_token\"}"),
                BackPackHttpReply.Ok("{}"));
            var service = new BackPackService(() => XsollaId, Endpoint(), transport,
                userTokenProvider: force => Task.FromResult(force ? "fresh" : "stale"));

            BackPackRewardsReply reply = await service.GetRewardsAsync();

            Assert.IsTrue(reply.Success);
            Assert.AreEqual(2, transport.Calls.Count);
            Assert.AreEqual("stale", transport.Calls[0].UserToken);
            Assert.AreEqual("fresh", transport.Calls[1].UserToken);
        }

        [Test]
        public async Task GetRewards_TokenRefusedAgain_DoesNotLoop()
        {
            var transport = new FakeBackPackTransport(
                BackPackHttpReply.Status(401), BackPackHttpReply.Status(401), BackPackHttpReply.Ok("{}"));
            var service = new BackPackService(() => XsollaId, Endpoint(), transport,
                userTokenProvider: force => Task.FromResult(force ? "fresh" : "stale"));

            BackPackRewardsReply reply = await service.GetRewardsAsync();

            Assert.IsFalse(reply.Success);
            Assert.AreEqual(401, reply.StatusCode);
            Assert.AreEqual(2, transport.Calls.Count);
        }
    }
}
