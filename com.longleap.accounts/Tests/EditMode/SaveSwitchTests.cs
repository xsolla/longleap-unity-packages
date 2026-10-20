using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;

namespace LongLeap.Accounts.Tests
{
    public class SaveSwitchTests
    {
        // An in-memory save service. Every call completes synchronously, so the
        // switch never has to wait for a frame and tests can block on it.
        private sealed class FakeCloud : ICloudSaveService, ICloudSaveBulkAccess
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public readonly List<string> Writes = new List<string>();
            public readonly List<string> Deletes = new List<string>();
            public int Resets;

            public Task InitializeAsync() => Task.CompletedTask;

            public Task SetStringAsync(CloudSaveKey key, string value)
            {
                Values[key.Value] = value;
                Writes.Add(key.Value);
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(CloudSaveKey key) =>
                Task.FromResult(Values.TryGetValue(key.Value, out var v) ? v : null);

            public Task<bool> HasKeyAsync(CloudSaveKey key) => Task.FromResult(Values.ContainsKey(key.Value));

            public Task DeleteKeyAsync(CloudSaveKey key)
            {
                Values.Remove(key.Value);
                Deletes.Add(key.Value);
                return Task.CompletedTask;
            }

            public Task DeleteAllAsync()
            {
                Values.Clear();
                return Task.CompletedTask;
            }

            public Task ResetAsync()
            {
                Resets++;
                Values.Clear();
                return Task.CompletedTask;
            }

            public Task<IReadOnlyDictionary<string, CloudSaveEntry>> SnapshotAsync()
            {
                var snapshot = new Dictionary<string, CloudSaveEntry>();

                foreach (var pair in Values)
                    snapshot[pair.Key] = new CloudSaveEntry(pair.Value, 100);

                return Task.FromResult<IReadOnlyDictionary<string, CloudSaveEntry>>(snapshot);
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeScope : IAccountCloudSaveScope
        {
            public bool IsAccountScoped => !string.IsNullOrEmpty(AccountId);
            public string AccountId { get; private set; } = "";
            public void UseAccount(string accountId) => AccountId = accountId;
            public void UseGuest() => AccountId = "";

            public Task<IReadOnlyDictionary<string, CloudSaveEntry>> PeekAccountAsync(string accountId) =>
                Task.FromResult<IReadOnlyDictionary<string, CloudSaveEntry>>(new Dictionary<string, CloudSaveEntry>());
        }

        private FakeCloud _cloud;
        private FakeScope _scope;
        private SaveSwitch _switch;

        [SetUp]
        public void SetUp()
        {
            _cloud = new FakeCloud();
            _scope = new FakeScope();

            var rules = new SaveMergeRules(SaveMergeRule.PreferAccount)
                .Add("coins", SaveMergeRule.Max)
                .Add("skins", SaveMergeRule.Union);

            _switch = new SaveSwitch(_cloud, _cloud, _scope, rules, _cloud.ResetAsync);
        }

        private SaveSwitchOutcome Run(string accountId, Dictionary<string, CloudSaveEntry> account)
        {
            Task<SaveSwitchOutcome> task = _switch.RunAtStartupAsync(accountId, account);
            Assert.IsTrue(task.IsCompleted, "fakes are synchronous — the switch must not wait");
            return task.Result;
        }

        private static Dictionary<string, CloudSaveEntry> Save(params (string key, string value)[] pairs)
        {
            var save = new Dictionary<string, CloudSaveEntry>();

            foreach (var (key, value) in pairs)
                save[key] = new CloudSaveEntry(value, 100);

            return save;
        }

        [Test]
        public void Guest_OnGuestSave_ChangesNothing()
        {
            _cloud.Values["coins"] = "5";

            Assert.AreEqual(SaveSwitchOutcome.Unchanged, Run(null, null));
            Assert.AreEqual("5", _cloud.Values["coins"]);
        }

        [Test]
        public void Guest_OnAccountSave_ClearsIt()
        {
            _scope.UseAccount("acc-1");
            _cloud.Values["coins"] = "5";

            Assert.AreEqual(SaveSwitchOutcome.ClearedForGuest, Run(null, null));
            Assert.AreEqual(1, _cloud.Resets);
            Assert.IsFalse(_scope.IsAccountScoped);
        }

        [Test]
        public void OtherAccount_ClearsAndSwitches()
        {
            _scope.UseAccount("acc-1");
            _cloud.Values["coins"] = "5";

            Assert.AreEqual(SaveSwitchOutcome.ClearedForOtherAccount, Run("acc-2", Save(("coins", "1"))));
            Assert.AreEqual(1, _cloud.Resets);
            Assert.AreEqual("acc-2", _scope.AccountId);
        }

        [Test]
        public void SameAccount_RegularRelaunch_ChangesNothing()
        {
            _scope.UseAccount("acc-1");
            _cloud.Values["coins"] = "5";

            Assert.AreEqual(SaveSwitchOutcome.Unchanged, Run("acc-1", Save(("coins", "5"))));
            CollectionAssert.IsEmpty(_cloud.Writes);
        }

        [Test]
        public void SameAccount_EmptyServer_UploadsLocal()
        {
            _scope.UseAccount("acc-1");
            _cloud.Values["coins"] = "5";

            Assert.AreEqual(SaveSwitchOutcome.UploadedLocal, Run("acc-1", Save()));
            CollectionAssert.AreEqual(new[] { "coins" }, _cloud.Writes);
        }

        [Test]
        public void GuestSignsIn_MergesByRules()
        {
            _cloud.Values["coins"] = "50";
            _cloud.Values["skins"] = "red";
            _cloud.Values["colors"] = "guest";

            var account = Save(("coins", "30"), ("skins", "blue"), ("colors", "acc"), ("level", "3"));

            Assert.AreEqual(SaveSwitchOutcome.Merged, Run("acc-1", account));
            Assert.AreEqual("acc-1", _scope.AccountId);

            Assert.AreEqual("50", _cloud.Values["coins"]);
            Assert.AreEqual("blue\nred", _cloud.Values["skins"]);
            Assert.AreEqual("acc", _cloud.Values["colors"]);

            // Missing locally and equal to the account value — initialization
            // pulls it from the cloud, so the switch doesn't write it.
            CollectionAssert.DoesNotContain(_cloud.Writes, "level");
        }

        [Test]
        public void GuestSignsIn_UnreadableAccount_KeepsGuest()
        {
            _cloud.Values["coins"] = "50";

            Assert.AreEqual(SaveSwitchOutcome.Failed, Run("acc-1", null));
            Assert.IsFalse(_scope.IsAccountScoped);
            Assert.AreEqual("50", _cloud.Values["coins"]);
        }

        [Test]
        public void CanUseAccount()
        {
            Assert.IsFalse(SaveSwitch.CanUseAccount(null, true, _scope), "guest");
            Assert.IsTrue(SaveSwitch.CanUseAccount("acc-1", true, _scope), "account save was read");
            Assert.IsFalse(SaveSwitch.CanUseAccount("acc-1", false, _scope), "not read, guest mirror");

            _scope.UseAccount("acc-1");
            Assert.IsTrue(SaveSwitch.CanUseAccount("acc-1", false, _scope), "not read, but the mirror is this account's");
        }

        [Test]
        public void WriteSet_SkipsUnchangedAndPulledKeys()
        {
            var target = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "3", ["d"] = null };
            var local = Save(("a", "1"), ("c", "0"), ("d", "x"));
            var account = Save(("a", "1"), ("b", "2"), ("c", "3"));

            CollectionAssert.AreEqual(new[] { "c", "d" }, SaveSwitch.BuildWriteSet(target, local, account));
        }
    }
}
