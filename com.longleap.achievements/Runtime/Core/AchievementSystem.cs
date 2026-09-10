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
        private readonly UnlockPropagator _propagator = new();

        private IAchievementProvider _provider;
        private IAchievementStateStore _stateStore;
        private IAchievementGameBridge _bridge;
        private IStatSystem _stats;
        private readonly Dictionary<AchievementId, List<StatObjectiveBinding>> _statBindings = new();

        private bool _initialized;

        public async Task Initialize(IAchievementProvider provider, IAchievementStateStore stateStore, IAchievementGameBridge gameBridge = null, IStatSystem stats = null)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (stateStore == null) throw new ArgumentNullException(nameof(stateStore));

            _provider = provider;
            _stateStore = stateStore;
            _bridge = gameBridge;
            _stats = stats;

            _state.Clear();
            _statBindings.Clear();

            var definitions = await provider.LoadAsync();
            _state.LoadDefinitions(definitions);

            BuildObjectiveLookups(definitions);

            var snapshot = await stateStore.LoadAsync(definitions);
            _state.ReconcileSnapshot(snapshot);

            foreach (var entry in snapshot?.Entries ?? Array.Empty<AchievementStateEntry>())
            {
                if (entry.IsUnlocked)
                    _propagator.Enqueue(new AchievementId(entry.AchievementId));
            }

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
            _initialized = true;
        }

        private void BuildObjectiveLookups(AchievementDefinition[] definitions)
        {
            foreach (var def in definitions)
            {
                if (def.Objectives == null) continue;
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
                        _propagator.Enqueue(achId);

                    _stats.Subscribe(b.Path, (changedPath, value) =>
                    {
                        // value — это значение ОДНОГО изменившегося листа, а цель у
                        // пути с '*' («kill:*:enemy1a») собирается со всех сразу.
                        // Взяв value как есть, мы теряли килы остальных героев:
                        // 30 монахом + 25 ниндзя давали не 55, а 25 (последний
                        // лист), и цель в 50 закрывалась только внутри одного
                        // героя. Для конкретного пути value и есть агрегат —
                        // лишний проход по дереву там не нужен.
                        long current = b.Path.IsPattern ? _stats.GetAggregate(b.Path) : value;

                        b.CachedValue = current;
                        _state.SetProgressOverride(achId, b.ObjectiveId, (int)current);

                        if (AreAllStatObjectivesComplete(achId) && !_state.IsUnlocked(achId))
                        {
                            _propagator.Enqueue(achId);
                            _state.MarkDirty();
                            DrainUnlockQueue();
                        }
                    });
                }
            }
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

            _stats?.UnsubscribeAll();

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

        public void ResetAll()
        {
            _stats?.UnsubscribeAll();

            _state.Clear();
            _statBindings.Clear();

            if (_initialized)
            {
                _ = _stateStore.DeleteAllAsync();
                _state.ComputeAvailability();
                if (_stats != null)
                    SubscribeToStats();
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
            if (!_state.TryMarkUnlocked(achievementId)) return;

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            PersistUnlockAsync(achievementId, timestamp);

            var definition = _state.Definitions.TryGetValue(achievementId, out var def)
                ? def
                : new AchievementDefinition { Id = achievementId };
            try { _bridge?.OnUnlocked(definition); }
            catch (Exception e) { Debug.LogError($"AchievementSystem: bridge OnUnlocked error: {e.Message}"); }

            _propagator.Enqueue(achievementId);
        }

        private void DrainUnlockQueue()
        {
            var processed = new HashSet<AchievementId>();

            while (_propagator.HasPending)
            {
                var allPending = _propagator.GetAllPending();
                _propagator.Clear();

                foreach (var id in allPending)
                {
                    if (!processed.Add(id)) continue;

                    CheckAndUnlock(id);

                    var deps = _state.CheckPrerequisiteUnlockDeps(id);
                    foreach (var depId in deps)
                    {
                        if (!_state.IsUnlocked(depId))
                            InternalUnlock(depId);
                    }
                }

                foreach (var kvp in _state.Definitions)
                {
                    var otherId = kvp.Key;
                    if (!processed.Add(otherId)) continue;
                    if (_state.IsUnlocked(otherId)) continue;

                    var otherDef = kvp.Value;
                    if (otherDef.Prerequisites != null && otherDef.Prerequisites.Length > 0 &&
                        otherDef.Prerequisites.All(p => _state.IsUnlocked(p)))
                    {
                        CheckAndUnlock(otherId);
                    }
                }
            }
        }

        private void CheckAndUnlock(AchievementId id)
        {
            if (_state.IsUnlocked(id)) return;

            if (!_state.Definitions.TryGetValue(id, out var def))
            {
                if (!_state.IsAvailable(id))
                    _state.SetAvailable(id);
                return;
            }

            bool hasStatObjective = _statBindings.ContainsKey(id);
            bool hasRequiredObjective = def.Objectives != null &&
                def.Objectives.Any(o => o.Required != null && o.Required.Length > 0);

            bool prereqsMet = def.Prerequisites == null || def.Prerequisites.Length == 0 ||
                def.Prerequisites.All(p => _state.IsUnlocked(p));

            if (!prereqsMet) return;

            if (!_state.IsAvailable(id))
            {
                _state.SetAvailable(id);
                try { _bridge?.OnAvailable(id); }
                catch (Exception e) { Debug.LogError($"AchievementSystem: bridge OnAvailable error for '{id.Value}': {e.Message}"); }
            }

            bool shouldUnlock = false;
            if (!hasStatObjective && !hasRequiredObjective)
                shouldUnlock = true;
            else if (hasStatObjective && AreAllStatObjectivesComplete(id))
                shouldUnlock = true;
            else if (hasRequiredObjective && def.Objectives
                         .Where(o => o.Required != null && o.Required.Length > 0)
                         .All(o => o.Required.All(r => _state.IsUnlocked(r))))
                shouldUnlock = true;

            if (shouldUnlock)
                InternalUnlock(id);
        }

        private async void PersistUnlockAsync(AchievementId achievementId, long timestamp)
        {
            try
            {
                await _stateStore.MarkUnlockedAsync(achievementId, timestamp);
            }
            catch (Exception e)
            {
                Debug.LogError($"AchievementSystem: ошибка сохранения достижения '{achievementId.Value}': {e.Message}");
            }
        }
    }
}
