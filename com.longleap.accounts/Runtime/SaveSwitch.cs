using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using UnityEngine;

namespace LongLeap.Accounts
{
    // What the save switch did at startup. For logs and tests.
    public enum SaveSwitchOutcome
    {
        // The local save already belonged to this launch's owner.
        Unchanged,

        // The player is a guest, but the local save belonged to an account —
        // it was cleared. The account's cloud save is untouched.
        ClearedForGuest,

        // The local save belonged to another account — it was cleared.
        ClearedForOtherAccount,

        // The local save belonged to this account, but the server had nothing —
        // the local copy was uploaded.
        UploadedLocal,

        // Guest progress was merged into the account by the merge rules.
        Merged,

        // Something went wrong; the player keeps the local save as it was.
        Failed
    }

    // Whose save is used in this launch — decided ONCE, at startup.
    //
    // The owner of the local save mirror is known before the save service
    // starts (see AccountGate), and an account change always goes through a
    // restart, so nothing is switched on the fly. The whole job: find out
    // whose data sits in the local mirror and bring it in line with this
    // launch's owner — clear another owner's data, and when a guest signs in,
    // merge the guest progress into the account by the game's merge rules.
    //
    // Ported from Blood Red Snow's SaveSwitch. The difference: instead of
    // asking the player which save to keep, the saves are merged key by key
    // (SaveMergeRules), so the switch knows nothing about the game.
    //
    // Run BEFORE CloudSaveService.InitializeAsync: initialization merges the
    // mirror with the cloud by timestamps, and after that there would be
    // nothing left to compare. Never throws: any failure is logged and the
    // player keeps what they have.
    public sealed class SaveSwitch
    {
        // How long to wait for a single save operation.
        private const double StepTimeoutSec = 5;

        // Applying writes many keys, so its limit is derived from their count.
        // Writes are local-first and reach the cloud in a batch, so this limit
        // is a safety net, not the expected duration.
        private const double ApplySecondsPerKey = 0.1;
        private const double ApplyTimeoutMinSec = 10;
        private const double ApplyTimeoutMaxSec = 60;

        private readonly ICloudSaveService _cloudSave;
        private readonly ICloudSaveBulkAccess _bulk;
        private readonly IAccountCloudSaveScope _scope;
        private readonly SaveMergeRules _rules;
        private readonly Func<Task> _resetLocalMirror;

        // resetLocalMirror — forget the local copy of the save WITHOUT touching
        // the cloud (CloudSaveService.ResetLocalCacheAsync).
        public SaveSwitch(
            ICloudSaveService cloudSave,
            ICloudSaveBulkAccess bulk,
            IAccountCloudSaveScope scope,
            SaveMergeRules rules,
            Func<Task> resetLocalMirror)
        {
            _cloudSave = cloudSave ?? throw new ArgumentNullException(nameof(cloudSave));
            _bulk = bulk ?? throw new ArgumentNullException(nameof(bulk));
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _resetLocalMirror = resetLocalMirror ?? throw new ArgumentNullException(nameof(resetLocalMirror));
        }

        public SaveSwitchOutcome LastOutcome { get; private set; } = SaveSwitchOutcome.Unchanged;

        // Can this launch run on the account's cloud save.
        //
        // It can't if the local mirror doesn't belong to this account and the
        // account save could not be read: there is nothing to merge with, and
        // going ahead could overwrite one of the two progresses. The account's
        // own mirror needs no comparison — it runs local-first without the
        // network.
        public static bool CanUseAccount(string accountId, bool accountSaveKnown, IAccountCloudSaveScope scope)
        {
            if (string.IsNullOrEmpty(accountId))
                return false;

            if (accountSaveKnown)
                return true;

            return scope != null && scope.IsAccountScoped && scope.AccountId == accountId;
        }

        // accountId — who signed in (empty means guest). accountEntries — the
        // account save on the server; null means it could not be read.
        public async Task<SaveSwitchOutcome> RunAtStartupAsync(
            string accountId,
            IReadOnlyDictionary<string, CloudSaveEntry> accountEntries)
        {
            try
            {
                LastOutcome = await RunCoreAsync(accountId, accountEntries);
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(SaveSwitch)}: failed — keeping the local save as it is. {e}");
                LastOutcome = SaveSwitchOutcome.Failed;
            }

            return LastOutcome;
        }

        private async Task<SaveSwitchOutcome> RunCoreAsync(
            string accountId,
            IReadOnlyDictionary<string, CloudSaveEntry> accountEntries)
        {
            // 1. Guest. If the mirror holds an account save (the player signed
            //    out or the session expired), it doesn't belong to the guest —
            //    clear it. The account's cloud save is left untouched.
            if (string.IsNullOrEmpty(accountId))
            {
                if (!_scope.IsAccountScoped)
                    return SaveSwitchOutcome.Unchanged;

                Debug.Log($"{nameof(SaveSwitch)}: the local save belongs to an account, but the player is a guest — clearing it.");

                _scope.UseGuest();
                await WaitStepAsync(_resetLocalMirror(), "reset the local save");

                return SaveSwitchOutcome.ClearedForGuest;
            }

            // 2. The mirror already belongs to this account — a regular relaunch.
            //
            // One adjustment: if the server has nothing for this account (a new
            // server, or the save was wiped there), service initialization won't
            // upload the mirror by itself — it only pushes dirty keys — so the
            // local progress is uploaded explicitly.
            if (_scope.IsAccountScoped && _scope.AccountId == accountId)
            {
                if (accountEntries == null || accountEntries.Count > 0)
                    return SaveSwitchOutcome.Unchanged;

                IReadOnlyDictionary<string, CloudSaveEntry> own = await SnapshotAsync();

                if (own == null || own.Count == 0)
                    return SaveSwitchOutcome.Unchanged;

                Debug.Log($"{nameof(SaveSwitch)}: the account save is empty on the server — uploading the local copy.");

                var upload = new Dictionary<string, string>(own.Count, StringComparer.Ordinal);

                foreach (var pair in own)
                    upload[pair.Key] = pair.Value.Value;

                await ApplyAsync(upload, own, accountEntries, forceWrite: true);
                return SaveSwitchOutcome.UploadedLocal;
            }

            // 3. The mirror belongs to another account. That progress lives in
            //    the other account's cloud save — just clear the local copy.
            if (_scope.IsAccountScoped)
            {
                Debug.Log($"{nameof(SaveSwitch)}: the local save belongs to another account — clearing it.");

                _scope.UseAccount(accountId);
                await WaitStepAsync(_resetLocalMirror(), "reset the local save");

                return SaveSwitchOutcome.ClearedForOtherAccount;
            }

            // 4. A guest mirror and the player has signed in: merge. Without the
            //    account save there is nothing to merge with — see CanUseAccount;
            //    the account gate never gets here.
            if (accountEntries == null)
            {
                Debug.LogError($"{nameof(SaveSwitch)}: the account save is unknown — keeping the guest save, nothing switched.");
                return SaveSwitchOutcome.Failed;
            }

            IReadOnlyDictionary<string, CloudSaveEntry> local =
                await SnapshotAsync() ?? new Dictionary<string, CloudSaveEntry>();

            Dictionary<string, string> merged = _rules.Merge(local, accountEntries);

            Debug.Log($"{nameof(SaveSwitch)}: merging the guest save ({local.Count} keys) " +
                $"into the account save ({accountEntries.Count} keys).");

            // From here on the mirror belongs to the account.
            _scope.UseAccount(accountId);

            await ApplyAsync(merged, local, accountEntries, forceWrite: false);
            return SaveSwitchOutcome.Merged;
        }

        // Bring the local mirror (and through it the cloud) to the target values.
        //
        // A key is skipped when nothing would change: it already has the target
        // value both locally and in the account, or it is missing locally and
        // the target IS the account value — service initialization pulls it
        // from the cloud by itself. Stats and achievements have hundreds of
        // keys, and pushing unchanged ones back would be pure traffic.
        //
        // forceWrite — write every key regardless (the server lost the save).
        private async Task ApplyAsync(
            IReadOnlyDictionary<string, string> target,
            IReadOnlyDictionary<string, CloudSaveEntry> local,
            IReadOnlyDictionary<string, CloudSaveEntry> account,
            bool forceWrite)
        {
            List<string> keys = BuildWriteSet(target, local, account, forceWrite);

            if (keys.Count == 0)
            {
                Debug.Log($"{nameof(SaveSwitch)}: the saves already match — nothing to write.");
                return;
            }

            double timeout = Math.Min(ApplyTimeoutMaxSec, Math.Max(ApplyTimeoutMinSec, keys.Count * ApplySecondsPerKey));

            Task apply = WriteAsync(keys, target);

            if (!await FrameWait.ForAsync(apply, timeout))
            {
                Debug.LogError($"{nameof(SaveSwitch)}: applying {keys.Count} keys exceeded {timeout} s — " +
                    "anything not written yet keeps its old value.");
                return;
            }

            if (apply.IsFaulted)
                Debug.LogError($"{nameof(SaveSwitch)}: applying failed — {apply.Exception?.GetBaseException().Message}");
        }

        private async Task WriteAsync(List<string> keys, IReadOnlyDictionary<string, string> target)
        {
            int written = 0;
            int deleted = 0;

            foreach (string key in keys)
            {
                var cloudKey = new CloudSaveKey(key);
                target.TryGetValue(key, out string value);

                if (value != null)
                {
                    await _cloudSave.SetStringAsync(cloudKey, value);
                    written++;
                }
                else
                {
                    await _cloudSave.DeleteKeyAsync(cloudKey);
                    deleted++;
                }
            }

            Debug.Log($"{nameof(SaveSwitch)}: applied — {written} written, {deleted} deleted.");
        }

        // Keys that have to be written or deleted. Stable order, so logs and
        // tests read the same from run to run.
        public static List<string> BuildWriteSet(
            IReadOnlyDictionary<string, string> target,
            IReadOnlyDictionary<string, CloudSaveEntry> local,
            IReadOnlyDictionary<string, CloudSaveEntry> account,
            bool forceWrite = false)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);

            if (target != null)
            {
                foreach (var pair in target)
                    keys.Add(pair.Key);
            }

            var result = new List<string>();

            foreach (string key in keys)
            {
                string want = target[key];
                string localValue = Value(local, key);
                string accountValue = Value(account, key);

                if (!forceWrite)
                {
                    bool same = Equal(want, localValue) && Equal(want, accountValue);
                    bool pulledByInit = localValue == null && want != null && Equal(want, accountValue);

                    if (same || pulledByInit)
                        continue;

                    // Nothing to delete anywhere.
                    if (want == null && localValue == null && accountValue == null)
                        continue;
                }
                else if (want == null)
                {
                    continue;
                }

                result.Add(key);
            }

            return result;
        }

        private async Task<IReadOnlyDictionary<string, CloudSaveEntry>> SnapshotAsync()
        {
            Task<IReadOnlyDictionary<string, CloudSaveEntry>> task;

            try
            {
                task = _bulk.SnapshotAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(SaveSwitch)}: failed to read the local save — {e.Message}");
                return null;
            }

            if (!await FrameWait.ForAsync(task, StepTimeoutSec))
            {
                Debug.LogError($"{nameof(SaveSwitch)}: reading the local save exceeded {StepTimeoutSec} s.");
                return null;
            }

            if (task.IsFaulted)
            {
                Debug.LogError($"{nameof(SaveSwitch)}: failed to read the local save — " +
                    $"{task.Exception?.GetBaseException().Message}");
                return null;
            }

            return task.Result;
        }

        private static async Task WaitStepAsync(Task task, string what)
        {
            if (!await FrameWait.ForAsync(task, StepTimeoutSec))
            {
                Debug.LogError($"{nameof(SaveSwitch)}: '{what}' exceeded {StepTimeoutSec} s.");
                return;
            }

            if (task != null && task.IsFaulted)
                Debug.LogError($"{nameof(SaveSwitch)}: '{what}' failed — {task.Exception?.GetBaseException().Message}");
        }

        private static string Value(IReadOnlyDictionary<string, CloudSaveEntry> source, string key) =>
            source != null && source.TryGetValue(key, out CloudSaveEntry entry) ? entry.Value : null;

        private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    }
}
