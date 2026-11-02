using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.Stats;
using UnityEngine;

namespace LongLeap.Achievements
{
    public sealed class AchievementSystem : IAchievementSystem
    {
        private sealed class StatObjectiveBinding
        {
            public StatPath Path;
            public int Target;
            public ObjectiveId ObjectiveId;
            public long CachedValue;
        }

        private readonly StateManager _state = new();

        // The queue holds two kinds of ids: still locked ones are unlock
        // candidates, already unlocked ones need their dependents re-checked
        // (see DrainUnlockQueue).
        private readonly Queue<AchievementId> _pending = new();

        private IAchievementProvider _provider;
        private IAchievementStateStore _stateStore;
        private IAchievementGameBridge _bridge;
        private IStatSystem _stats;
        private readonly Dictionary<AchievementId, List<StatObjectiveBinding>> _statBindings = new();
        // Our own subscriptions, so we remove only those: IStatSystem is shared,
        // other systems (DailyQuestService) subscribe to it too, and
        // UnsubscribeAll would wipe their subscriptions as well.
        private readonly List<(StatPath Path, Action<StatPath, long> Callback)> _subscriptions = new();

        // Save deletion started by ResetAll. Unlock writes wait for it, otherwise
        // a late deletion would erase achievements unlocked right after the reset.
        private Task _pendingDelete = Task.CompletedTask;

        private bool _initialized;

        public async Task Initialize(IAchievementProvider provider, IAchievementStateStore stateStore, IAchievementGameBridge gameBridge = null, IStatSystem stats = null)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (stateStore == null) throw new ArgumentNullException(nameof(stateStore));

            UnsubscribeFromStats();

            _provider = provider;
            _stateStore = stateStore;
            _bridge = gameBridge;
            _stats = stats;

            _state.Clear();
            _statBindings.Clear();
            _pending.Clear();

            // Validate the raw data, so the log names what the provider actually
            // returned (null entries, duplicates) before Normalize drops it.
            var loaded = await provider.LoadAsync();
            LogValidation(AchievementValidator.Validate(loaded));
            var definitions = Normalize(loaded);

            _state.LoadDefinitions(definitions);

            BuildObjectiveLookups(definitions);

            var snapshot = await stateStore.LoadAsync(definitions);
            _state.ReconcileSnapshot(snapshot);

            foreach (var id in definitions.Select(d => d.Id).Where(_state.IsUnlocked))
                _pending.Enqueue(id);

            Evaluate();
            _initialized = true;
        }

        // A bad config does not stop the system: the game should still start.
        // Achievements hit by an error may never unlock, so errors go to the log
        // loudly.
        private static void LogValidation(ValidationResult result)
        {
            foreach (var warning in result.Warnings)
                Debug.LogWarning($"AchievementSystem: {warning}");
            foreach (var error in result.Errors)
                Debug.LogError($"AchievementSystem: {error}");
        }

        // Brings provider data to one shape, so the rest of the system never
        // checks for null: no null arrays or entries, no achievements without an
        // id, no None ids in Prerequisites/Required. On duplicate ids the first
        // definition wins, the same one the validator points at as the original.
        // Definitions are copied: the provider's objects are left untouched.
        private static AchievementDefinition[] Normalize(AchievementDefinition[] definitions)
        {
            var result = new List<AchievementDefinition>();
            var seen = new HashSet<AchievementId>();

            foreach (var def in definitions ?? Array.Empty<AchievementDefinition>())
            {
                if (def == null || def.Id.IsNone || !seen.Add(def.Id)) continue;

                result.Add(new AchievementDefinition
                {
                    Id = def.Id,
                    Group = def.Group,
                    IsHidden = def.IsHidden,
                    Prerequisites = NormalizeIds(def.Prerequisites),
                    Objectives = (def.Objectives ?? Array.Empty<Objective>())
                        .Where(o => o != null)
                        .Select(o => new Objective
                        {
                            Id = o.Id,
                            StatPath = o.StatPath,
                            StatTarget = o.StatTarget,
                            Required = NormalizeIds(o.Required)
                        })
                        .ToArray()
                });
            }

            return result.ToArray();
        }

        private static AchievementId[] NormalizeIds(AchievementId[] ids) =>
            ids?.Where(id => !id.IsNone).ToArray() ?? Array.Empty<AchievementId>();

        // Subscribes to stats and re-evaluates the whole state: what is available
        // and what should unlock. Shared by Initialize and ResetAll.
        private void Evaluate()
        {
            if (_stats != null)
                SubscribeToStats();

            _state.ComputeAvailability();

            var (autoUnlocks, notifications) = _state.ComputeAutoUnlocksWithNotifications();
            foreach (var id in notifications)
            {
                try { _bridge?.OnAvailable(id); }
                catch (Exception e) { Debug.LogError($"AchievementSystem: bridge OnAvailable error for '{id.Value}': {e.Message}"); }
            }
            foreach (var id in autoUnlocks)
                InternalUnlock(id);

            DrainUnlockQueue();
        }

        private void BuildObjectiveLookups(AchievementDefinition[] definitions)
        {
            foreach (var def in definitions)
            {
                foreach (var obj in def.Objectives)
                {
                    if (string.IsNullOrEmpty(obj.StatPath)) continue;

                    if (!_statBindings.TryGetValue(def.Id, out var bindings))
                    {
                        bindings = new List<StatObjectiveBinding>();
                        _statBindings[def.Id] = bindings;
                    }
                    bindings.Add(new StatObjectiveBinding
                    {
                        Path = new StatPath(obj.StatPath),
                        Target = obj.StatTarget,
                        ObjectiveId = obj.Id,
                        CachedValue = 0
                    });
                }
            }
        }

        private void SubscribeToStats()
        {
            foreach (var kvp in _statBindings)
            {
                var achId = kvp.Key;
                var bindings = kvp.Value;

                foreach (var binding in bindings)
                {
                    var b = binding;
                    long initialValue = _stats.GetAggregate(b.Path);
                    b.CachedValue = initialValue;
                    _state.SetProgressOverride(achId, b.ObjectiveId, (int)initialValue);

                    if (initialValue >= b.Target && AreAllStatObjectivesComplete(achId) && !_state.IsUnlocked(achId))
                        _pending.Enqueue(achId);

                    Action<StatPath, long> callback = (changedPath, value) =>
                    {
                        // value is the value of the ONE leaf that changed, while a
                        // goal on a '*' path ("kill:*:enemy1a") sums all of them.
                        // Taking value as is lost the other heroes' kills:
                        // 30 as monk + 25 as ninja gave 25 (the last leaf), not 55,
                        // so a goal of 50 could only be reached within a single
                        // hero. For a concrete path value already is the
                        // aggregate, so the extra tree walk is not needed there.
                        long current = b.Path.IsPattern ? _stats.GetAggregate(b.Path) : value;

                        b.CachedValue = current;
                        _state.SetProgressOverride(achId, b.ObjectiveId, (int)current);

                        if (AreAllStatObjectivesComplete(achId) && !_state.IsUnlocked(achId))
                        {
                            _pending.Enqueue(achId);
                            _state.MarkDirty();
                            DrainUnlockQueue();
                        }
                    };

                    _stats.Subscribe(b.Path, callback);
                    _subscriptions.Add((b.Path, callback));
                }
            }
        }

        private void UnsubscribeFromStats()
        {
            if (_stats != null)
            {
                foreach (var (path, callback) in _subscriptions)
                    _stats.Unsubscribe(path, callback);
            }
            _subscriptions.Clear();
        }

        private bool AreAllStatObjectivesComplete(AchievementId id)
        {
            if (!_statBindings.TryGetValue(id, out var bindings)) return false;
            foreach (var b in bindings)
            {
                if (b.CachedValue < b.Target)
                    return false;
            }
            return true;
        }

        public void Shutdown()
        {
            if (!_initialized) return;

            UnsubscribeFromStats();

            _initialized = false;
            _provider = null;
            _stateStore = null;
            _bridge = null;
            _stats = null;
        }

        public void ForceUnlock(AchievementId achievementId)
        {
            if (!_initialized) return;
            if (_state.IsUnlocked(achievementId)) return;

            InternalUnlock(achievementId);
            DrainUnlockQueue();
        }

        // Resets unlocks, but not definitions or stat bindings: afterwards the
        // system is in the same state as after Initialize with an empty save.
        // ResetAll does not touch stats, so achievements whose goals are already
        // met unlock again right away.
        public void ResetAll()
        {
            UnsubscribeFromStats();
            _pending.Clear();
            _state.Clear();

            if (!_initialized) return;

            _pendingDelete = DeleteAllStateAsync(_stateStore);
            Evaluate();
        }

        private static async Task DeleteAllStateAsync(IAchievementStateStore store)
        {
            try
            {
                await store.DeleteAllAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"AchievementSystem: failed to delete saved achievement state: {e.Message}");
            }
        }

        public AchievementStatus[] GetAll() => _state.GetAll();
        public AchievementStatus[] GetByGroup(GroupId groupId) => _state.GetByGroup(groupId);
        public AchievementStatus[] GetAvailable() => _state.GetAvailable();
        public AchievementStatus[] GetAchieved() => _state.GetAchieved();
        public AchievementStatus[] GetPartiallyAchieved() => _state.GetPartiallyAchieved();
        public AchievementStatus GetStatus(AchievementId achievementId) => _state.GetStatus(achievementId);
        public GroupId[] GetGroups() => _state.GetGroups();

        private void InternalUnlock(AchievementId achievementId)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!_state.TryMarkUnlocked(achievementId, timestamp)) return;

            PersistUnlockAsync(achievementId, timestamp);

            var definition = _state.Definitions.TryGetValue(achievementId, out var def)
                ? def
                : new AchievementDefinition { Id = achievementId };
            try { _bridge?.OnUnlocked(definition); }
            catch (Exception e) { Debug.LogError($"AchievementSystem: bridge OnUnlocked error: {e.Message}"); }

            _pending.Enqueue(achievementId);
        }

        // InternalUnlock puts the unlocked achievement back in the queue, so the
        // chain A -> B -> C drains fully regardless of definition order. Each
        // achievement unlocks at most once, so the loop terminates.
        private void DrainUnlockQueue()
        {
            while (_pending.Count > 0)
            {
                var id = _pending.Dequeue();

                if (!_state.IsUnlocked(id))
                {
                    CheckAndUnlock(id);
                    continue;
                }

                foreach (var dependentId in _state.GetDependents(id))
                    CheckAndUnlock(dependentId);
            }
        }

        private void CheckAndUnlock(AchievementId id)
        {
            if (_state.IsUnlocked(id)) return;
            if (!_state.Definitions.TryGetValue(id, out var def)) return;

            if (!def.Prerequisites.All(_state.IsUnlocked)) return;

            if (!_state.IsAvailable(id))
            {
                _state.SetAvailable(id);
                try { _bridge?.OnAvailable(id); }
                catch (Exception e) { Debug.LogError($"AchievementSystem: bridge OnAvailable error for '{id.Value}': {e.Message}"); }
            }

            // Unlock only when ALL objectives are complete, both stat-driven and
            // dependency-based. The checks used to be else-if, so an achievement
            // with both kinds unlocked on stats alone and the list was never checked.
            bool statsComplete = !_statBindings.ContainsKey(id) || AreAllStatObjectivesComplete(id);
            bool requiredComplete = def.Objectives.All(o => o.Required.All(_state.IsUnlocked));

            if (statsComplete && requiredComplete)
                InternalUnlock(id);
        }

        private async void PersistUnlockAsync(AchievementId achievementId, long timestamp)
        {
            // Capture the store now: Shutdown may null the field while we wait for the deletion.
            var store = _stateStore;
            try
            {
                await _pendingDelete;
                await store.MarkUnlockedAsync(achievementId, timestamp);
            }
            catch (Exception e)
            {
                Debug.LogError($"AchievementSystem: failed to save achievement '{achievementId.Value}': {e.Message}");
            }
        }
    }
}
