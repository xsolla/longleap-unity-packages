using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaCloudSaveProviderTests
    {
        private const string BaseUrl = "http://127.0.0.1:5066";
        private const string GameId = "test_game";
        private const string AccountId = "account-1";
        private const string Query = "?xsollaId=" + AccountId + "&gameId=" + GameId;

        private static readonly CloudSaveKey Key = new CloudSaveKey("coins");

        private static XsollaCloudSaveProvider Create(FakeTransport transport, string savesPath = null) =>
            new XsollaCloudSaveProvider(BaseUrl + "/", GameId, AccountId, transport.AsHttp(), savesPath);

        private static Dictionary<string, CloudSaveEntry> Entries(params (string key, string value, long ts)[] rows)
        {
            var entries = new Dictionary<string, CloudSaveEntry>();

            foreach (var row in rows)
                entries[row.key] = new CloudSaveEntry(row.value, row.ts);

            return entries;
        }

        // ---- constructor ----

        [TestCase(null, GameId, AccountId)]
        [TestCase(" ", GameId, AccountId)]
        [TestCase(BaseUrl, "", AccountId)]
        [TestCase(BaseUrl, GameId, null)]
        [TestCase(BaseUrl, GameId, "  ")]
        public void Constructor_RejectsEmptyArguments(string baseUrl, string gameId, string accountId)
        {
            Assert.Throws<ArgumentException>(() => new XsollaCloudSaveProvider(baseUrl, gameId, accountId));
        }

        [Test]
        public void AccountId_IsTheOneGiven()
        {
            Assert.AreEqual(AccountId, Create(new FakeTransport()).AccountId);
        }

        // ---- per-key ----

        [Test]
        public async Task SetString_PutsTheUnpackedEnvelopeForTheAccount()
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));

            await Create(transport).SetStringAsync(Key, CloudSaveEnvelope.Serialize("100", 1760000000));

            HttpCall call = transport.Calls[0];
            Assert.AreEqual("PUT", call.Method);
            Assert.AreEqual(BaseUrl + "/saves/coins", call.Url, "the account goes in the body, not the query");
            Assert.AreEqual("application/json", call.ContentType);

            var body = JObject.Parse(call.Body);
            Assert.AreEqual(GameId, (string)body["gameId"]);
            Assert.AreEqual(AccountId, (string)body["xsollaId"]);
            Assert.AreEqual("100", (string)body["v"]);
            Assert.AreEqual(1760000000, (long)body["ts"]);
        }

        // Values written before the envelope existed go up as is, time unknown.
        [Test]
        public async Task SetString_PlainValue_IsSentWithZeroTimestamp()
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));

            await Create(transport).SetStringAsync(Key, "legacy");

            var body = JObject.Parse(transport.Calls[0].Body);
            Assert.AreEqual("legacy", (string)body["v"]);
            Assert.AreEqual(0, (long)body["ts"]);
        }

        [Test]
        public void SetString_Throws_OnFailure()
        {
            var transport = new FakeTransport(HttpReply.Status(500, "boom"));

            Assert.ThrowsAsync<InvalidOperationException>(() =>
                Create(transport).SetStringAsync(Key, CloudSaveEnvelope.Serialize("1", 1)));
        }

        [Test]
        public async Task KeyUrl_EscapesTheKey()
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));

            await Create(transport).SetStringAsync(new CloudSaveKey("slot 1?"), "x");

            Assert.AreEqual(BaseUrl + "/saves/slot%201%3F", transport.Calls[0].Url);
        }

        [TestCase(null, "/saves")]
        [TestCase("", "/saves")]
        [TestCase("  ", "/saves")]
        [TestCase("cloud", "/cloud")]
        [TestCase("cloud/", "/cloud")]
        [TestCase(" /v2/saves/ ", "/v2/saves")]
        public async Task SavesPath_IsNormalized(string savesPath, string expected)
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));

            await Create(transport, savesPath).SetStringAsync(Key, "x");

            Assert.AreEqual(BaseUrl + expected + "/coins", transport.Calls[0].Url);
        }

        // What SetString sends, GetString hands back as the same envelope.
        [Test]
        public async Task GetString_RoundTripsTheEnvelope()
        {
            string envelope = CloudSaveEnvelope.Serialize("100", 1760000000);
            var transport = new FakeTransport(HttpReply.Ok("{}"));
            XsollaCloudSaveProvider provider = Create(transport);

            await provider.SetStringAsync(Key, envelope);
            transport.WhenEmpty = HttpReply.Ok(transport.Calls[0].Body);

            string read = await provider.GetStringAsync(Key);

            Assert.AreEqual("GET", transport.Calls[1].Method);
            Assert.AreEqual(BaseUrl + "/saves/coins" + Query, transport.Calls[1].Url);
            Assert.AreEqual(envelope, read);
        }

        [Test]
        public async Task GetString_MissingTimestamp_IsZero()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"v\":\"100\"}"));

            Assert.AreEqual(CloudSaveEnvelope.Serialize("100", 0), await Create(transport).GetStringAsync(Key));
        }

        [TestCase(404)]
        [TestCase(500)]
        public async Task GetString_IsNull_OnFailure(int status)
        {
            var transport = new FakeTransport(HttpReply.Status(status));

            Assert.IsNull(await Create(transport).GetStringAsync(Key));
        }

        [Test]
        public async Task GetString_IsNull_OnGarbage()
        {
            var transport = new FakeTransport(HttpReply.Ok("<html></html>"));

            Assert.IsNull(await Create(transport).GetStringAsync(Key));
        }

        [Test]
        public async Task HasKey_FollowsGetString()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"v\":\"1\",\"ts\":1}"), HttpReply.Status(404));
            XsollaCloudSaveProvider provider = Create(transport);

            Assert.IsTrue(await provider.HasKeyAsync(Key));
            Assert.IsFalse(await provider.HasKeyAsync(Key));
        }

        [Test]
        public async Task DeleteKey_SendsDeleteWithTheAccountInTheQuery()
        {
            var transport = new FakeTransport(HttpReply.Ok(""));

            await Create(transport).DeleteKeyAsync(Key);

            HttpCall call = transport.Calls[0];
            Assert.AreEqual("DELETE", call.Method);
            Assert.AreEqual(BaseUrl + "/saves/coins" + Query, call.Url);
            Assert.IsNull(call.Body);
            Assert.IsNull(call.ContentType);
        }

        [Test]
        public void DeleteKey_Throws_OnFailure()
        {
            var transport = new FakeTransport(HttpReply.Transport("Cannot connect"));

            Assert.ThrowsAsync<InvalidOperationException>(() => Create(transport).DeleteKeyAsync(Key));
        }

        // ---- whole save ----

        [Test]
        public async Task DeleteAll_SendsDeleteToTheCollection()
        {
            var transport = new FakeTransport(HttpReply.Ok(""));

            await Create(transport).DeleteAllAsync();

            Assert.AreEqual("DELETE", transport.Calls[0].Method);
            Assert.AreEqual(BaseUrl + "/saves" + Query, transport.Calls[0].Url);
        }

        [Test]
        public async Task LoadAll_PacksEntriesIntoEnvelopes()
        {
            var transport = new FakeTransport(HttpReply.Ok(
                "{\"coins\":{\"v\":\"100\",\"ts\":5},\"level\":{\"v\":\"3\",\"ts\":7}}"));

            Dictionary<string, string> all = await Create(transport).LoadAllAsync();

            Assert.AreEqual("GET", transport.Calls[0].Method);
            Assert.AreEqual(BaseUrl + "/saves" + Query, transport.Calls[0].Url);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(CloudSaveEnvelope.Serialize("100", 5), all["coins"]);
            Assert.AreEqual(CloudSaveEnvelope.Serialize("3", 7), all["level"]);
        }

        // Called from bootstrap: a failure must not throw.
        [Test]
        public async Task LoadAll_IsEmpty_OnFailure()
        {
            var transport = new FakeTransport(HttpReply.Transport("Cannot connect"));

            Dictionary<string, string> all = await Create(transport).LoadAllAsync();

            Assert.IsNotNull(all);
            Assert.AreEqual(0, all.Count);
        }

        [Test]
        public async Task TryLoadEntries_IsNull_OnFailure()
        {
            var transport = new FakeTransport(HttpReply.Status(500), HttpReply.Ok("not json"));
            XsollaCloudSaveProvider provider = Create(transport);

            Assert.IsNull(await provider.TryLoadEntriesAsync(), "a server error is \"unknown\"");
            Assert.IsNull(await provider.TryLoadEntriesAsync(), "so is an unreadable body");
        }

        [Test]
        public async Task TryLoadEntries_IsEmpty_ForAnEmptySave()
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));

            IReadOnlyDictionary<string, CloudSaveEntry> entries = await Create(transport).TryLoadEntriesAsync();

            Assert.IsNotNull(entries, "an empty save is not a failure");
            Assert.AreEqual(0, entries.Count);
        }

        [Test]
        public async Task TryLoadEntries_SkipsMalformedEntries()
        {
            var transport = new FakeTransport(HttpReply.Ok(
                "{\"good\":{\"v\":\"x\",\"ts\":5}," +
                "\"noTs\":{\"v\":\"y\"}," +
                "\"noValue\":{\"ts\":1}," +
                "\"notAnObject\":\"z\"," +
                "\"nullEntry\":null}"));

            IReadOnlyDictionary<string, CloudSaveEntry> entries = await Create(transport).TryLoadEntriesAsync();

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("x", entries["good"].Value);
            Assert.AreEqual(5, entries["good"].TimestampUnixSeconds);
            Assert.AreEqual("y", entries["noTs"].Value);
            Assert.AreEqual(0, entries["noTs"].TimestampUnixSeconds);
        }

        // ---- batch ----

        [Test]
        public async Task SetMany_PutsEntriesAndDeletesInOneRequest()
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));

            await Create(transport).SetManyAsync(
                Entries(("coins", "100", 5), ("level", "3", 7)),
                new[] { "gone" });

            Assert.AreEqual(1, transport.Calls.Count);

            HttpCall call = transport.Calls[0];
            Assert.AreEqual("PUT", call.Method);
            Assert.AreEqual(BaseUrl + "/saves", call.Url);
            Assert.AreEqual("application/json", call.ContentType);
            Assert.AreEqual(60, call.TimeoutSeconds, "a batch can weigh megabytes");

            var body = JObject.Parse(call.Body);
            Assert.AreEqual(GameId, (string)body["gameId"]);
            Assert.AreEqual(AccountId, (string)body["xsollaId"]);

            var entries = (JObject)body["entries"];
            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual("100", (string)entries["coins"]["v"]);
            Assert.AreEqual(5, (long)entries["coins"]["ts"]);
            Assert.AreEqual(JTokenType.Null, entries["gone"].Type, "null means delete");
        }

        [Test]
        public async Task SetString_UsesTheDefaultTimeout()
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));

            await Create(transport).SetStringAsync(Key, "x");

            Assert.AreEqual(0, transport.Calls[0].TimeoutSeconds);
        }

        // One oversized value must not keep the whole batch out of the cloud.
        [Test]
        public async Task SetMany_SkipsValuesOverTheServerLimit()
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));
            LogAssert.Expect(LogType.Error, new Regex("'big' is .* over the server limit"));

            await Create(transport).SetManyAsync(
                Entries(("big", new string('x', 1024 * 1024 + 1), 1), ("limit", new string('x', 1024 * 1024), 1)),
                null);

            var entries = (JObject)JObject.Parse(transport.Calls[0].Body)["entries"];
            Assert.IsNull(entries["big"]);
            Assert.IsNotNull(entries["limit"], "exactly the limit still fits");
        }

        [Test]
        public async Task SetMany_SendsNothing_WhenNothingIsLeft()
        {
            var transport = new FakeTransport(HttpReply.Ok("{}"));
            XsollaCloudSaveProvider provider = Create(transport);
            LogAssert.Expect(LogType.Error, new Regex("over the server limit"));

            await provider.SetManyAsync(null, null);
            await provider.SetManyAsync(new Dictionary<string, CloudSaveEntry>(), new string[0]);
            await provider.SetManyAsync(Entries(("big", new string('x', 1024 * 1024 + 1), 1)), null);

            Assert.AreEqual(0, transport.Calls.Count);
        }

        [Test]
        public void SetMany_Throws_OnFailure()
        {
            var transport = new FakeTransport(HttpReply.Status(413));

            Assert.ThrowsAsync<InvalidOperationException>(() =>
                Create(transport).SetManyAsync(Entries(("coins", "1", 1)), null));
        }
    }
}
