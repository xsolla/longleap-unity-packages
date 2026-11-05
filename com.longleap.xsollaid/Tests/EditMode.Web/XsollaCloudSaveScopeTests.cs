using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaCloudSaveScopeTests
    {
        private const string BaseUrl = "http://127.0.0.1:5066";
        private const string GameId = "test_game";

        // Same key as FirebaseCloudSaveProvider's, on purpose.
        private const string ScopePrefsKey = "longleap_cloudsave_scope";

        private InMemoryPlayerPrefs _prefs;

        [SetUp]
        public void SetUp() => _prefs = new InMemoryPlayerPrefs();

        private XsollaCloudSaveScope Create(FakeTransport transport = null, string baseUrl = BaseUrl, string gameId = GameId) =>
            new XsollaCloudSaveScope(baseUrl, gameId, _prefs, (transport ?? new FakeTransport()).AsHttp());

        [Test]
        public void Constructor_RejectsNullPrefs()
        {
            Assert.Throws<ArgumentNullException>(() => new XsollaCloudSaveScope(BaseUrl, GameId, null));
        }

        [Test]
        public void NewScope_IsGuest()
        {
            XsollaCloudSaveScope scope = Create();

            Assert.IsFalse(scope.IsAccountScoped);
            Assert.AreEqual("", scope.AccountId);
        }

        // The next launch must know whose data sits in the mirror.
        [Test]
        public void UseAccount_IsPersistedAndReloadedByANewInstance()
        {
            Create().UseAccount("account-1");

            XsollaCloudSaveScope reloaded = Create();

            Assert.AreEqual("account-1", _prefs.GetString(ScopePrefsKey, null));
            Assert.IsTrue(reloaded.IsAccountScoped);
            Assert.AreEqual("account-1", reloaded.AccountId);
        }

        [TestCase(null)]
        [TestCase("")]
        public void UseAccount_RejectsEmpty(string accountId)
        {
            XsollaCloudSaveScope scope = Create();

            Assert.Throws<ArgumentException>(() => scope.UseAccount(accountId));
            Assert.IsFalse(scope.IsAccountScoped);
        }

        [Test]
        public void UseGuest_DeletesTheKey()
        {
            XsollaCloudSaveScope scope = Create();
            scope.UseAccount("account-1");

            scope.UseGuest();

            Assert.IsFalse(_prefs.HasKey(ScopePrefsKey));
            Assert.IsFalse(scope.IsAccountScoped);
            Assert.IsFalse(Create().IsAccountScoped, "a new instance sees the guest too");
        }

        [Test]
        public async Task TryPeekAccount_ReadsTheAccountSaveWithoutTouchingTheScope()
        {
            var transport = new FakeTransport(HttpReply.Ok("{\"coins\":{\"v\":\"100\",\"ts\":5}}"));
            XsollaCloudSaveScope scope = Create(transport);

            IReadOnlyDictionary<string, CloudSaveEntry> entries = await scope.TryPeekAccountAsync("account-2");

            Assert.AreEqual(BaseUrl + "/saves?xsollaId=account-2&gameId=" + GameId, transport.Calls[0].Url);
            Assert.AreEqual("100", entries["coins"].Value);
            Assert.IsFalse(scope.IsAccountScoped, "peeking does not claim the mirror");
        }

        [TestCase(null, BaseUrl, GameId)]
        [TestCase("", BaseUrl, GameId)]
        [TestCase("account-1", null, GameId)]
        [TestCase("account-1", " ", GameId)]
        [TestCase("account-1", BaseUrl, null)]
        [TestCase("account-1", BaseUrl, "")]
        public async Task TryPeekAccount_IsNull_WhenSomethingIsMissing(string accountId, string baseUrl, string gameId)
        {
            var transport = new FakeTransport();

            Assert.IsNull(await Create(transport, baseUrl, gameId).TryPeekAccountAsync(accountId));
            Assert.AreEqual(0, transport.Calls.Count);
        }

        [Test]
        public async Task TryPeekAccount_IsNull_OnFailure()
        {
            var transport = new FakeTransport(HttpReply.Status(500));

            Assert.IsNull(await Create(transport).TryPeekAccountAsync("account-1"));
        }

        // The interface contract: never null, a failure looks like an empty save.
        [Test]
        public async Task PeekAccount_IsEmptyNotNull_OnFailure()
        {
            var transport = new FakeTransport(HttpReply.Transport("Cannot connect"));

            IReadOnlyDictionary<string, CloudSaveEntry> entries = await Create(transport).PeekAccountAsync("account-1");

            Assert.IsNotNull(entries);
            Assert.AreEqual(0, entries.Count);
        }
    }
}
