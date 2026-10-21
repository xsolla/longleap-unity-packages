using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.Leagues;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaLeagueProviderTests
    {
        private const string BaseUrl = "http://127.0.0.1:5066";
        private const string GameId = "Darksliders";
        private const string AccountId = "eea78aad-3764-4f71-b4d6-a4d8f591f62d";
        private const string League = "dev_weekly";

        // A reply as RewardService sends it: joined, in a group of three,
        // with a result of the previous period still unclaimed.
        private const string StateJson = @"{
            ""leagueId"": ""dev_weekly"",
            ""tiers"": [""backyard"", ""sidewalk"", ""alleyway""],
            ""period"": { ""number"": 4, ""startsAt"": 1792368000000, ""endsAt"": 1792972800000 },
            ""tier"": 1,
            ""group"": {
                ""number"": 2, ""promotePlaces"": 5, ""demoteFromPlace"": 16,
                ""place"": 2, ""score"": 900, ""scoreToPromotion"": 0,
                ""entries"": [
                    { ""place"": 1, ""displayName"": ""Bot 1234"", ""score"": 1500, ""isMe"": false, ""reward"": 100 },
                    { ""place"": 2, ""displayName"": ""Tester"", ""score"": 900, ""isMe"": true },
                    { ""place"": 3, ""displayName"": ""Bot 5678"", ""score"": 10, ""isMe"": false }
                ]
            },
            ""lastResult"": { ""periodNumber"": 3, ""place"": 4, ""score"": 5000, ""tier"": 0, ""newTier"": 1,
                              ""outcome"": ""promoted"", ""reward"": 70, ""claimed"": false },
            ""unclaimed"": [
                { ""periodNumber"": 3, ""place"": 4, ""score"": 5000, ""tier"": 0, ""newTier"": 1,
                  ""outcome"": ""promoted"", ""reward"": 70, ""claimed"": false }
            ]
        }";

        private sealed class FakeTransport
        {
            private readonly Queue<HttpReply> _replies = new Queue<HttpReply>();

            public readonly List<HttpCall> Calls = new List<HttpCall>();

            public FakeTransport(params HttpReply[] replies)
            {
                foreach (HttpReply reply in replies)
                    _replies.Enqueue(reply);
            }

            public XsollaIdHttp AsHttp() => new XsollaIdHttp(call =>
            {
                Calls.Add(call);

                return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : HttpReply.Status(500));
            });
        }

        private static XsollaLeagueProvider Create(FakeTransport transport) =>
            new XsollaLeagueProvider(BaseUrl + "/", GameId, AccountId, transport.AsHttp());

        [Test]
        public async Task GetState_ReadsTheWholeState()
        {
            var transport = new FakeTransport(HttpReply.Ok(StateJson));

            LeagueState state = await Create(transport).GetStateAsync(League);

            Assert.AreEqual("GET", transport.Calls[0].Method);
            Assert.AreEqual(
                BaseUrl + "/leagues/dev_weekly?gameId=Darksliders&xsollaId=" + AccountId,
                transport.Calls[0].Url);

            Assert.AreEqual("sidewalk", state.TierId);
            Assert.AreEqual(4, state.Period.Number);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1792972800000), state.Period.EndsAt);

            Assert.IsTrue(state.IsJoined);
            Assert.AreEqual(2, state.Group.Place);
            Assert.AreEqual(900, state.Group.Score);
            Assert.AreEqual(3, state.Group.Entries.Count);
            Assert.IsTrue(state.Group.Entries[1].IsMe);
            Assert.AreEqual(100, state.Group.Entries[0].Reward);
            Assert.AreEqual(0, state.Group.Entries[1].Reward, "a row without the field has no reward");
            Assert.AreEqual(LeagueZone.Promotion, state.Group.ZoneOf(2));

            Assert.AreEqual(LeagueOutcome.Promoted, state.LastResult.Outcome);
            Assert.AreEqual(70, state.LastResult.Reward);
            Assert.AreEqual(1, state.Unclaimed.Count);
        }

        [Test]
        public async Task GetState_NotJoined_HasNoGroup()
        {
            var transport = new FakeTransport(HttpReply.Ok(
                "{\"leagueId\":\"dev_weekly\",\"tiers\":[\"backyard\"],\"period\":{\"number\":1,\"startsAt\":0,\"endsAt\":0}," +
                "\"tier\":0,\"group\":null,\"lastResult\":null,\"unclaimed\":[]}"));

            LeagueState state = await Create(transport).GetStateAsync(League);

            Assert.IsFalse(state.IsJoined);
            Assert.IsNull(state.LastResult);
            Assert.AreEqual("backyard", state.TierId);
        }

        [Test]
        public async Task Submit_PostsScoreAndNameForTheAccount()
        {
            var transport = new FakeTransport(HttpReply.Ok(StateJson));

            await Create(transport).SubmitScoreAsync(League, 1500, "Tester");

            HttpCall call = transport.Calls[0];
            Assert.AreEqual("POST", call.Method);
            Assert.AreEqual(BaseUrl + "/leagues/dev_weekly/scores", call.Url);
            Assert.AreEqual("application/json", call.ContentType);

            var body = JObject.Parse(call.Body);
            Assert.AreEqual(GameId, (string)body["gameId"]);
            Assert.AreEqual(AccountId, (string)body["xsollaId"]);
            Assert.AreEqual(1500, (long)body["score"]);
            Assert.AreEqual("Tester", (string)body["displayName"]);
        }

        [Test]
        public async Task Claim_PostsThePeriodAndReadsTheReply()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"claimed\":true,\"reward\":70}"));

            LeagueClaim claim = await Create(transport).ClaimAsync(League, 3);

            Assert.AreEqual(BaseUrl + "/leagues/dev_weekly/results/3/claim", transport.Calls[0].Url);
            Assert.IsTrue(claim.Claimed);
            Assert.AreEqual(70, claim.Reward);
        }

        [Test]
        public async Task DebugFill_NotJoined_ReturnsNull()
        {
            var transport = new FakeTransport(HttpReply.Status(409, "{\"error\":\"not_joined\"}"));

            Assert.IsNull(await Create(transport).DebugFillGroupAsync(League));
            Assert.AreEqual(BaseUrl + "/leagues/dev_weekly/debug/fill", transport.Calls[0].Url);
        }

        [Test]
        public async Task DebugFill_ReadsTheStateFromTheReply()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"added\":17,\"state\":" + StateJson + "}"));

            LeagueState state = await Create(transport).DebugFillGroupAsync(League);

            Assert.AreEqual(3, state.Group.Entries.Count);
        }

        [Test]
        public void FailedRequest_Throws()
        {
            var transport = new FakeTransport(HttpReply.Status(500, "boom"));

            Assert.ThrowsAsync<InvalidOperationException>(() => Create(transport).GetStateAsync(League));
        }
    }
}
