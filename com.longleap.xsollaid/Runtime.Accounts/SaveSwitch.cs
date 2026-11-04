using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using UnityEngine;

namespace LongLeap.XsollaId.Accounts
{
    /// <summary>
    /// What the save switch did at startup.
    /// </summary>
    public enum SaveSwitchOutcome
    {
        /// <summary>The local save already belonged to this launch's owner.</summary>
        Unchanged,

        /// <summary>
        /// The player is a guest, but the local save belonged to an account — it
        /// was cleared. The account's cloud save is untouched.
        /// </summary>
        ClearedForGuest,

        /// <summary>The local save belonged to another account — it was cleared.</summary>
        ClearedForOtherAccount,

        /// <summary>
        /// The local save belonged to this account, but the server had nothing —
        /// the local copy was uploaded.
        /// </summary>
        UploadedLocal,

        /// <summary>Guest progress was merged into the account by the merge rules.</summary>
        Merged,

        /// <summary>
        /// The local save could not be brought in line with this launch's owner.
        /// The scope was NOT switched, so the next launch tries again. For an
        /// account, play this launch on a local-only save: initializing the
        /// account's cloud save on a mirror that isn't the account's would push
        /// the mirror into it. See <see cref="SaveSwitchOutcomeExtensions.IsFailure"/>.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Helpers for <see cref="SaveSwitchOutcome"/>.
    /// </summary>
    public static class SaveSwitchOutcomeExtensions
    {
        /// <summary>
        /// The switch failed: for an account, don't connect the cloud save in this
        /// launch (use a local-only provider). A guest needs nothing — its saves
        /// are local anyway.
        /// </summary>
        public static bool IsFailure(this SaveSwitchOutcome outcome) => outcome == SaveSwitchOutcome.Failed;
    }

    /// <summary>
    /// Whose save is used in this launch — decided ONCE, at startup.
    /// </summary>
    /// <remarks>
    /// The owner of the local save mirror is known before the save service
    /// starts (see <see cref="AccountGate"/>), and an account change always goes
    /// through a restart, so nothing is switched on the fly. The whole job: find
    /// out whose data sits in the local mirror and bring it in line with this
    /// launch's owner — clear another owner's data, and when a guest signs in,
    /// merge the guest progress into the account key by key
    /// (<see cref="SaveMergeRules"/>), so the switch knows nothing about the
    /// game.
    ///
    /// The scope is switched LAST, only after the mirror really holds the new
    /// owner's data. A failure in between leaves the scope as it was, so the
    /// next launch sees the same situation and does the same work again (the
    /// merge is safe to repeat).
    ///
    /// Run BEFORE <c>CloudSaveService.InitializeAsync</c>: initialization merges
    /// the mirror with the cloud by timestamps, and after that there would be
    /// nothing left to compare. Never throws: any failure is logged and reported
    /// as <see cref="SaveSwitchOutcome.Failed"/>.
    /// </remarks>
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

        /// <summary>
        /// The usual setup: the save service is the writer, the snapshot source
        /// and the local reset at once.
        /// </summary>
        /// <param name="saves">The save service, not initialized yet.</param>
        /// <param name="scope">Whose save sits in the local mirror.</param>
        /// <param name="rules">How guest progress is merged into the account.</param>
        public SaveSwitch(CloudSaveService saves, IAccountCloudSaveScope scope, SaveMergeRules rules)
            : this(saves, saves, scope, rules, saves != null ? saves.ResetLocalCacheAsync : (Func<Task>)null)
        {
        }

        /// <param name="cloudSave">The save service the merged values are written through.</param>
        /// <param name="bulk">Reads the local mirror in one snapshot.</param>
        /// <param name="scope">Whose save sits in the local mirror.</param>
        /// <param name="rules">How guest progress is merged into the account.</param>
        /// <param name="resetLocalMirror">Forget the local copy of the save WITHOUT
        /// touching the cloud (<c>CloudSaveService.ResetLocalCacheAsync</c>).</param>
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

        /// <summary>The result of the last <see cref="RunAtStartupAsync"/>.</summary>
        public SaveSwitchOutcome LastOutcome { get; private set; } = SaveSwitchOutcome.Unchanged;

        /// <summary>
        /// Can this launch run on the account's cloud save.
        /// </summary>
        /// <remarks>
        /// It can't if the local mirror doesn't belong to this account and the
        /// account save could not be read: there is nothing to merge with, and
        /// going ahead could overwrite one of the two progresses. The account's
        /// own mirror needs no comparison — it runs local-first without the
        /// network.
        /// </remarks>
        /// <param name="accountSaveKnown">The account save was read from the server.</param>
        public static bool CanUseAccount(string accountId, bool accountSaveKnown, IAccountCloudSaveScope scope)
        {
            if (string.IsNullOrEmpty(accountId))
                return false;

            if (accountSaveKnown)
                return true;

            return scope != null && scope.IsAccountScoped && scope.AccountId == accountId;
        }

        /// <summary>
        /// Bring the local mirror in line with this launch's owner. Never throws.
        /// </summary>
        /// <param name="accountId">Who signed in; <c>null</c> or empty means guest.</param>
        /// <param name="accountEntries">The account save on the server; <c>null</c>
        /// means it could not be read.</param>
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
                Debug.LogError($"{nameof(SaveSwitch)}: failed — the save owner was not switched. {e}");
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

                if (!await ResetLocalMirrorAsync())
                    return SaveSwitchOutcome.Failed;

                _scope.UseGuest();
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

                if (own == null)
                    return SaveSwitchOutcome.Failed;

                if (own.Count == 0)
                    return SaveSwitchOutcome.Unchanged;

                Debug.Log($"{nameof(SaveSwitch)}: the account save is empty on the server — uploading the local copy.");

                var upload = new Dictionary<string, string>(own.Count, StringComparer.Ordinal);

                foreach (var pair in own)
                    upload[pair.Key] = pair.Value.Value;

                return await ApplyAsync(upload, own, accountEntries, forceWrite: true)
                    ? SaveSwitchOutcome.UploadedLocal
                    : SaveSwitchOutcome.Failed;
            }

            // 3. The mirror belongs to another account. That progress lives in
            //    the other account's cloud save — just clear the local copy.
            if (_scope.IsAccountScoped)
            {
                Debug.Log($"{nameof(SaveSwitch)}: the local save belongs to another account — clearing it.");

                if (!await ResetLocalMirrorAsync())
                    return SaveSwitchOutcome.Failed;

                _scope.UseAccount(accountId);
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

            IReadOnlyDictionary<string, CloudSaveEntry> local = await SnapshotAsync();

            // Merging with an unknown guest save would throw the guest progress away.
            if (local == null)
                return SaveSwitchOutcome.Failed;

            Dictionary<string, string> merged = _rules.Merge(local, accountEntries);

            Debug.Log($"{nameof(SaveSwitch)}: merging the guest save ({local.Count} keys) " +
                $"into the account save ({accountEntries.Count} keys).");

            if (!await ApplyAsync(merged, local, accountEntries, forceWrite: false))
                return SaveSwitchOutcome.Failed;

            // Only now does the mirror belong to the account.
            _scope.UseAccount(accountId);
            return SaveSwitchOutcome.Merged;
        }

        // Bring the local mirror (and through it the cloud) to the target values.
        // true — every key was written.
        //
        // A key is skipped when nothing would change: it already has the target
        // value both locally and in the account, or it is missing locally and
        // the target IS the account value — service initialization pulls it
        // from the cloud by itself. Stats and achievements have hundreds of
        // keys, and pushing unchanged ones back would be pure traffic.
        //
        // forceWrite — write every key regardless (the server lost the save).
        private async Task<bool> ApplyAsync(
            IReadOnlyDictionary<string, string> target,
            IReadOnlyDictionary<string, CloudSaveEntry> local,
            IReadOnlyDictionary<string, CloudSaveEntry> account,
            bool forceWrite)
        {
            List<string> keys = BuildWriteSet(target, local, account, forceWrite);

            if (keys.Count == 0)
            {
                Debug.Log($"{nameof(SaveSwitch)}: the saves already match — nothing to write.");
                return true;
            }

            double timeout = Math.Min(ApplyTimeoutMaxSec, Math.Max(ApplyTimeoutMinSec, keys.Count * ApplySecondsPerKey));

            Task apply = WriteAsync(keys, target);

            if (!await FrameWait.ForAsync(apply, timeout))
            {
                // The writes are not cancelled: they keep going in the background
                // and still land in the mirror. The switch reports failure because
                // it can't tell when they finish.
                Debug.LogError($"{nameof(SaveSwitch)}: applying {keys.Count} keys exceeded {timeout} s — " +
                    "the save owner was not switched; the remaining writes continue in the background.");
                return false;
            }

            if (apply.Status != TaskStatus.RanToCompletion)
            {
                Debug.LogError($"{nameof(SaveSwitch)}: applying failed — the save owner was not switched. " +
                    $"{apply.Exception?.GetBaseException().Message}");
                return false;
            }

            return true;
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

        /// <summary>
        /// Keys that have to be written or deleted to reach <paramref name="target"/>.
        /// Stable (ordinal) order, so logs and tests read the same from run to run.
        /// </summary>
        /// <param name="target">The values after the switch; <c>null</c> means delete.</param>
        /// <param name="forceWrite">Write every non-null key regardless of what
        /// the two saves hold.</param>
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

            if (task == null || task.Status != TaskStatus.RanToCompletion)
            {
                Debug.LogError($"{nameof(SaveSwitch)}: failed to read the local save — " +
                    $"{task?.Exception?.GetBaseException().Message ?? "no snapshot"}");
                return null;
            }

            return task.Result;
        }

        // Forget the local copy. true — it is gone.
        private async Task<bool> ResetLocalMirrorAsync()
        {
            Task task;

            try
            {
                task = _resetLocalMirror();
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(SaveSwitch)}: failed to reset the local save — the save owner was not switched. {e.Message}");
                return false;
            }

            if (!await FrameWait.ForAsync(task, StepTimeoutSec))
            {
                Debug.LogError($"{nameof(SaveSwitch)}: resetting the local save exceeded {StepTimeoutSec} s — the save owner was not switched.");
                return false;
            }

            if (task != null && task.Status != TaskStatus.RanToCompletion)
            {
                Debug.LogError($"{nameof(SaveSwitch)}: failed to reset the local save — the save owner was not switched. " +
                    $"{task.Exception?.GetBaseException().Message}");
                return false;
            }

            return true;
        }

        private static string Value(IReadOnlyDictionary<string, CloudSaveEntry> source, string key) =>
            source != null && source.TryGetValue(key, out CloudSaveEntry entry) ? entry.Value : null;

        private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    }
}
