using System;
using System.Threading.Tasks;
using LongLeap.Core.Leaderboards;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaLeaderboardProviderTests
    {
        private const string BaseUrl = "http://127.0.0.1:5066";
        private const string GameId = "test_game";
        private const string AccountId = "account-1";

        private static readonly BoardId Board = new BoardId("dev_score");

        private static XsollaLeaderboardProvider Create(FakeTransport transport) =>
            new XsollaLeaderboardProvider(BaseUrl + "/", GameId, AccountId, transport.AsHttp());

        [Test]
        public async Task Submit_PostsScoreAndNameForTheAccount()
        {
            var transport = new FakeTransport(
                HttpReply.Ok("{\"accepted\":true,\"best\":1500,\"rank\":1,\"total\":1}"));

            bool accepted = await Create(transport).SubmitScoreAsync(
                Board, "ignored-uid", 1500, "Tester", SortDirection.Descending);

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, transport.Calls.Count);

            HttpCall call = transport.Calls[0];
            Assert.AreEqual("POST", call.Method);
            Assert.AreEqual(BaseUrl + "/leaderboards/dev_score/scores", call.Url);
            Assert.AreEqual("application/json", call.ContentType);

            var body = JObject.Parse(call.Body);
            Assert.AreEqual(GameId, (string)body["gameId"]);
            Assert.AreEqual(AccountId, (string)body["xsollaId"], "the account is sent, not the uid argument");
            Assert.AreEqual(1500, (long)body["score"]);
            Assert.AreEqual("Tester", (string)body["displayName"]);
        }

        [Test]
        public async Task Submit_ReturnsFalse_WhenTheBestWasNotBeaten()
        {
            var transport = new FakeTransport(
                HttpReply.Ok("{\"accepted\":false,\"best\":2000,\"rank\":3,\"total\":10}"));

            bool accepted = await Create(transport).SubmitScoreAsync(
                Board, AccountId, 1500, "Tester", SortDirection.Descending);

            Assert.IsFalse(accepted);
        }

        // LeaderboardService re-queues the score only if the provider throws.
        [Test]
        public void Submit_Throws_OnServerError()
        {
            var transport = new FakeTransport(HttpReply.Status(500, "boom"));

            Assert.ThrowsAsync<InvalidOperationException>(() => Create(transport).SubmitScoreAsync(
                Board, AccountId, 1500, "Tester", SortDirection.Descending));
        }

        [Test]
        public async Task GetTop_MapsRows_AndGivesOnlyTheOwnRowTheAccountId()
        {
            var transport = new FakeTransport(HttpReply.Ok(
                "{\"boardId\":\"dev_score\",\"total\":5,\"entries\":[" +
                "{\"rank\":1,\"displayName\":\"Rival\",\"score\":2400,\"updatedAt\":1760000000000,\"isMe\":false}," +
                "{\"rank\":2,\"displayName\":\"Tester\",\"score\":1500,\"updatedAt\":1760000001000,\"isMe\":true}" +
                "],\"me\":{\"rank\":2,\"score\":1500}}"));

            LeaderboardPage page = await Create(transport).GetTopAsync(Board, SortDirection.Descending, 2);

            StringAssert.StartsWith(BaseUrl + "/leaderboards/dev_score?", transport.Calls[0].Url);
            StringAssert.Contains("gameId=" + GameId, transport.Calls[0].Url);
            StringAssert.Contains("xsollaId=" + AccountId, transport.Calls[0].Url);
            StringAssert.Contains("limit=2", transport.Calls[0].Url);

            Assert.AreEqual(2, page.Entries.Count);
            Assert.IsTrue(page.HasMore, "total 5 > 2 rows");

            LeaderboardEntry rival = page.Entries[0];
            Assert.AreEqual(1, rival.Rank);
            Assert.AreEqual("Rival", rival.DisplayName);
            Assert.AreEqual(2400, rival.Score);
            Assert.AreEqual(1760000000000, rival.Timestamp);
            Assert.AreNotEqual(AccountId, rival.Uid);

            LeaderboardEntry me = page.Entries[1];
            Assert.AreEqual(AccountId, me.Uid);
            Assert.AreEqual(2, me.Rank);
        }

        [Test]
        public async Task GetPlayerRank_AsksForNoRows_AndMapsMe()
        {
            var transport = new FakeTransport(HttpReply.Ok(
                "{\"boardId\":\"dev_score\",\"total\":42,\"entries\":[],\"me\":{\"rank\":17,\"score\":900}}"));

            PlayerRank rank = await Create(transport).GetPlayerRankAsync(Board, SortDirection.Descending, AccountId);

            StringAssert.Contains("limit=0", transport.Calls[0].Url);
            Assert.AreEqual(17, rank.Rank);
            Assert.AreEqual(900, rank.Score);
            Assert.AreEqual(42, rank.TotalPlayers);
        }

        [Test]
        public async Task GetPlayerRank_IsEmpty_WhenThePlayerHasNoScore()
        {
            var transport = new FakeTransport(HttpReply.Ok(
                "{\"boardId\":\"dev_score\",\"total\":42,\"entries\":[],\"me\":null}"));

            PlayerRank rank = await Create(transport).GetPlayerRankAsync(Board, SortDirection.Descending, AccountId);

            Assert.IsNull(rank.Rank);
            Assert.IsNull(rank.Score);
            Assert.AreEqual(42, rank.TotalPlayers);
        }

        [Test]
        public async Task Reads_ReturnEmpty_OnTransportError()
        {
            var transport = new FakeTransport(
                HttpReply.Transport("Cannot connect"),
                HttpReply.Transport("Cannot connect"));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("GetTopAsync"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("GetPlayerRankAsync"));

            var provider = Create(transport);
            LeaderboardPage page = await provider.GetTopAsync(Board, SortDirection.Descending, 10);
            PlayerRank rank = await provider.GetPlayerRankAsync(Board, SortDirection.Descending, AccountId);

            Assert.AreEqual(0, page.Entries.Count);
            Assert.IsNull(rank.Rank);
        }

        [Test]
        public async Task GetSurroundings_IsEmpty_WithoutARequest()
        {
            var transport = new FakeTransport();

            LeaderboardPage page = await Create(transport).GetSurroundingsAsync(
                Board, SortDirection.Descending, AccountId, 3);

            Assert.AreEqual(0, page.Entries.Count);
            Assert.AreEqual(0, transport.Calls.Count);
        }

        [Test]
        public async Task AccountAuth_ReturnsTheAccountAsUid()
        {
            var auth = new XsollaAccountAuthProvider(AccountId);

            var result = await auth.LoginAsync() as LongLeap.Core.Auth.AuthResult.Success;

            Assert.IsNotNull(result);
            Assert.AreEqual(AccountId, result.Uid.Value);
        }
    }
}
