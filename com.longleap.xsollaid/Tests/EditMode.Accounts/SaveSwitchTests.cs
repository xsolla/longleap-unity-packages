using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static LongLeap.XsollaId.Accounts.Tests.TestSaves;

namespace LongLeap.XsollaId.Accounts.Tests
{
    [TestFixture]
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
            public bool FailWrites;
            public bool FailReset;
            public bool FailSnapshot;
            public bool ThrowOnSnapshot;

            public Task InitializeAsync() => Task.CompletedTask;

            public Task SetStringAsync(CloudSaveKey key, string value)
            {
                if (FailWrites)
                    return Task.FromException(new System.InvalidOperationException("write failed"));

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
                if (FailReset)
                    return Task.FromException(new System.InvalidOperationException("reset failed"));

                Resets++;
                Values.Clear();
                return Task.CompletedTask;
            }

            public Task<IReadOnlyDictionary<string, CloudSaveEntry>> SnapshotAsync()
            {
                if (ThrowOnSnapshot)
                    throw new System.InvalidOperationException("snapshot threw");

                if (FailSnapshot)
                    return Task.FromException<IReadOnlyDictionary<string, CloudSaveEntry>>(
                        new System.InvalidOperationException("snapshot failed"));

                var snapshot = new Dictionary<string, CloudSaveEntry>();

                foreach (var pair in Values)
                    snapshot[pair.Key] = new CloudSaveEntry(pair.Value, 100);

                return Task.FromResult<IReadOnlyDictionary<string, CloudSaveEntry>>(snapshot);
            }

            public void Dispose()
            {
            }
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
                .Add("skins", SaveMergeRule.Union)
                .Add("tmp_*", (key, input) => null);

            _switch = new SaveSwitch(_cloud, _cloud, _scope, rules, _cloud.ResetAsync);
        }

        private SaveSwitchOutcome Run(string accountId, Dictionary<string, CloudSaveEntry> account)
        {
            Task<SaveSwitchOutcome> task = _switch.RunAtStartupAsync(accountId, account);
            Assert.IsTrue(task.IsCompleted, "fakes are synchronous — the switch must not wait");
            return task.Result;
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

            // The switch reports this as an error on purpose: the gate never lets it happen.
            LogAssert.Expect(LogType.Error, new Regex("the account save is unknown"));

            Assert.AreEqual(SaveSwitchOutcome.Failed, Run("acc-1", null));
            Assert.IsFalse(_scope.IsAccountScoped);
            Assert.AreEqual("50", _cloud.Values["coins"]);
        }

        [Test]
        public void GuestSignsIn_WriteFails_KeepsGuestScope()
        {
            _cloud.Values["coins"] = "50";
            _cloud.FailWrites = true;

            LogAssert.Expect(LogType.Error, new Regex("applying failed"));

            Assert.AreEqual(SaveSwitchOutcome.Failed, Run("acc-1", Save(("coins", "30"))));
            Assert.IsTrue(_switch.LastOutcome.IsFailure());
            Assert.IsFalse(_scope.IsAccountScoped, "the next launch must see a guest save and merge again");
        }

        [Test]
        public void OtherAccount_ResetFails_KeepsScope()
        {
            _scope.UseAccount("acc-1");
            _cloud.Values["coins"] = "5";
            _cloud.FailReset = true;

            LogAssert.Expect(LogType.Error, new Regex("failed to reset the local save"));

            Assert.AreEqual(SaveSwitchOutcome.Failed, Run("acc-2", Save(("coins", "1"))));
            Assert.AreEqual("acc-1", _scope.AccountId, "acc-1's data must not be labelled as acc-2's");
        }

        // mirrorOwner: "" is a guest mirror, null is no scope at all.
        [TestCase(null, true, "", ExpectedResult = false)]
        [TestCase("", true, "acc-1", ExpectedResult = false)]
        [TestCase("acc-1", true, "", ExpectedResult = true)]
        [TestCase("acc-1", false, "", ExpectedResult = false)]
        [TestCase("acc-1", false, "acc-2", ExpectedResult = false)]
        [TestCase("acc-1", false, "acc-1", ExpectedResult = true)]
        [TestCase("acc-1", false, null, ExpectedResult = false)]
        public bool CanUseAccount(string accountId, bool accountSaveKnown, string mirrorOwner)
        {
            if (mirrorOwner == null)
                return SaveSwitch.CanUseAccount(accountId, accountSaveKnown, null);

            if (mirrorOwner.Length > 0)
                _scope.UseAccount(mirrorOwner);

            return SaveSwitch.CanUseAccount(accountId, accountSaveKnown, _scope);
        }

        // Nothing on the server and nothing locally: nothing to upload.
        [Test]
        public void SameAccount_EmptyServerAndEmptyLocal_ChangesNothing()
        {
            _scope.UseAccount("acc-1");

            Assert.AreEqual(SaveSwitchOutcome.Unchanged, Run("acc-1", Save()));
            CollectionAssert.IsEmpty(_cloud.Writes);
            Assert.AreEqual("acc-1", _scope.AccountId);
        }

        // The server was not read: the own mirror runs local-first as it is.
        [Test]
        public void SameAccount_UnreadServer_ChangesNothing()
        {
            _scope.UseAccount("acc-1");
            _cloud.Values["coins"] = "5";

            Assert.AreEqual(SaveSwitchOutcome.Unchanged, Run("acc-1", null));
            CollectionAssert.IsEmpty(_cloud.Writes);
            Assert.AreEqual(0, _cloud.Resets);
        }

        // A rule returning null drops the key, locally and in the account.
        [Test]
        public void GuestSignsIn_RuleReturnsNull_DeletesTheKey()
        {
            _cloud.Values["tmp_local"] = "x";
            _cloud.Values["coins"] = "5";

            Assert.AreEqual(SaveSwitchOutcome.Merged, Run("acc-1", Save(("tmp_account", "y"))));
            CollectionAssert.AreEqual(new[] { "tmp_account", "tmp_local" }, _cloud.Deletes);
            Assert.IsFalse(_cloud.Values.ContainsKey("tmp_local"));
            CollectionAssert.AreEqual(new[] { "coins" }, _cloud.Writes);
        }

        // Merging with an unknown guest save would throw the guest progress away.
        [Test]
        public void GuestSignsIn_SnapshotFails_KeepsGuestScope([Values] bool throwsSynchronously)
        {
            _cloud.Values["coins"] = "50";
            _cloud.ThrowOnSnapshot = throwsSynchronously;
            _cloud.FailSnapshot = !throwsSynchronously;

            LogAssert.Expect(LogType.Error, new Regex("failed to read the local save"));

            Assert.AreEqual(SaveSwitchOutcome.Failed, Run("acc-1", Save(("coins", "30"))));
            Assert.IsFalse(_scope.IsAccountScoped);
            CollectionAssert.IsEmpty(_cloud.Writes);
        }

        [Test]
        public void SameAccount_EmptyServer_SnapshotFails_IsFailed()
        {
            _scope.UseAccount("acc-1");
            _cloud.Values["coins"] = "5";
            _cloud.FailSnapshot = true;

            LogAssert.Expect(LogType.Error, new Regex("failed to read the local save"));

            Assert.AreEqual(SaveSwitchOutcome.Failed, Run("acc-1", Save()));
            Assert.AreEqual("acc-1", _scope.AccountId);
            CollectionAssert.IsEmpty(_cloud.Writes);
        }

        [Test]
        public void WriteSet_SkipsUnchangedAndPulledKeys()
        {
            var target = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "3", ["d"] = null };
            var local = Save(("a", "1"), ("c", "0"), ("d", "x"));
            var account = Save(("a", "1"), ("b", "2"), ("c", "3"));

            CollectionAssert.AreEqual(new[] { "c", "d" }, SaveSwitch.BuildWriteSet(target, local, account));
        }

        // The server lost the save: every non-null key is written, even unchanged
        // ones; null keys are still skipped.
        [Test]
        public void WriteSet_ForceWrite_WritesEveryNonNullKey()
        {
            var target = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["d"] = null };
            var local = Save(("a", "1"), ("d", "x"));
            var account = Save(("a", "1"));

            CollectionAssert.AreEqual(new[] { "b", "d" }, SaveSwitch.BuildWriteSet(target, local, account));
            CollectionAssert.AreEqual(new[] { "a", "b" }, SaveSwitch.BuildWriteSet(target, local, account, forceWrite: true));
        }
    }
}
