using System;
using System.Collections.Generic;
using System.Linq;

namespace LongLeap.Achievements
{
    // Expects normalized definitions (see AchievementSystem.Normalize): no null
    // arrays, no null entries, unique ids.
    internal sealed class StateManager
    {
        // Unlocked achievement -> unlock time in Unix seconds, 0 if unknown.
        // Only achievements present in the definitions: saved unlocks of
        // achievements removed from the config are dropped on load, otherwise
        // they would show up in lists as entries with nothing but an id.
        private readonly Dictionary<AchievementId, long> _unlocked = new();
        private readonly HashSet<AchievementId> _available = new();
        private readonly Dictionary<AchievementId, AchievementDefinition> _definitions = new();
        // Who depends on an achievement: it is in their Prerequisites or in the
        // Required of one of their objectives. Built from definitions, so Clear()
        // leaves it alone.
        private readonly Dictionary<AchievementId, HashSet<AchievementId>> _dependents = new();
        // Progress of stat-driven objectives. The key is a PAIR (achievement,
        // objective), not the objective id alone: an id is unique only within its
        // achievement (exactly what AchievementValidator.CheckDuplicateObjectiveIds
        // checks), and in the config ids are short and repeat — "k" in every
        // kill_*, "h" in both big_hit_500 and heal_10. Keyed by the id alone, the
        // last achievement to write overwrote the others' progress, and the window
        // showed the total kill count for kill_50_grunts.
        private readonly Dictionary<(AchievementId, ObjectiveId), int> _progressOverrides = new();

        public bool IsDirty { get; private set; }
        public void MarkDirty() => IsDirty = true;
        public void ClearDirty() => IsDirty = false;
        public IReadOnlyDictionary<AchievementId, AchievementDefinition> Definitions => _definitions;

        public void LoadDefinitions(AchievementDefinition[] definitions)
        {
            _definitions.Clear();
            _dependents.Clear();
            foreach (var def in definitions)
            {
                _definitions[def.Id] = def;

                foreach (var prereq in def.Prerequisites)
                    AddDependent(prereq, def.Id);

                foreach (var obj in def.Objectives)
                {
                    foreach (var required in obj.Required)
                        AddDependent(required, def.Id);
                }
            }
        }

        private void AddDependent(AchievementId dependency, AchievementId dependent)
        {
            if (!_dependents.TryGetValue(dependency, out var set))
            {
                set = new HashSet<AchievementId>();
                _dependents[dependency] = set;
            }
            set.Add(dependent);
        }

        public IEnumerable<AchievementId> GetDependents(AchievementId achievementId) =>
            _dependents.TryGetValue(achievementId, out var set) ? set : Enumerable.Empty<AchievementId>();

        public void SetProgressOverride(AchievementId achievementId, ObjectiveId objectiveId, int value)
        {
            _progressOverrides[(achievementId, objectiveId)] = value;
        }

        public void Clear()
        {
            _unlocked.Clear();
            _available.Clear();
            _progressOverrides.Clear();
            IsDirty = false;
        }

        public bool IsUnlocked(AchievementId achId) => _unlocked.ContainsKey(achId);
        public bool IsAvailable(AchievementId achId) => _available.Contains(achId);

        public bool TryMarkUnlocked(AchievementId achId, long timestamp)
        {
            if (!_unlocked.TryAdd(achId, timestamp)) return false;
            _available.Remove(achId);
            IsDirty = true;
            return true;
        }

        public void SetAvailable(AchievementId achId) => _available.Add(achId);

        public bool IsFullyAvailable(AchievementId achId)
        {
            if (!_available.Contains(achId)) return false;
            if (_unlocked.ContainsKey(achId)) return false;
            return true;
        }

        public void ComputeAvailability()
        {
            _available.Clear();
            foreach (var kvp in _definitions)
            {
                if (_unlocked.ContainsKey(kvp.Key)) continue;
                if (kvp.Value.Prerequisites.All(_unlocked.ContainsKey))
                    _available.Add(kvp.Key);
            }
        }

        public (List<AchievementId> AutoUnlocks, List<AchievementId> Notifications) ComputeAutoUnlocksWithNotifications()
        {
            var autoUnlocks = new List<AchievementId>();
            var notifications = new List<AchievementId>();

            foreach (var id in _available.ToList())
            {
                if (!_definitions.TryGetValue(id, out var def)) continue;
                if (def.Objectives.Length == 0)
                    autoUnlocks.Add(id);
                else
                    notifications.Add(id);
            }

            return (autoUnlocks, notifications);
        }

        public AchievementStatus BuildStatus(AchievementId id)
        {
            if (!_definitions.TryGetValue(id, out var def))
                return null;

            var objProgresses = def.Objectives.Select(obj =>
            {
                bool isComplete = false;
                int current = 0;
                int target = 1;

                if (obj.Required.Length > 0)
                {
                    // A "list" objective (all_heroes, bestiary_complete) counts the
                    // required achievements themselves. It used to stay at
                    // current = 0 and target = 1, so progress was lost: the
                    // consumer had nothing to show "2 of 4" with, and it must treat
                    // an objective with target <= 1 as a done/not-done flag, not a
                    // counter.
                    target = obj.Required.Length;
                    current = obj.Required.Count(_unlocked.ContainsKey);
                    isComplete = current >= target;
                }
                else if (!string.IsNullOrEmpty(obj.StatPath))
                {
                    current = _progressOverrides.TryGetValue((id, obj.Id), out var cached) ? cached : 0;
                    target = obj.StatTarget;
                    isComplete = current >= target;
                }

                return new ObjectiveProgress
                {
                    ObjectiveId = obj.Id,
                    Progress = isComplete ? new Progress(target, target) : new Progress(current, target)
                };
            }).ToArray();

            bool isUnlocked = _unlocked.TryGetValue(id, out var timestamp);

            return new AchievementStatus
            {
                Definition = def,
                IsAvailable = _available.Contains(id),
                IsUnlocked = isUnlocked,
                UnlockedAt = isUnlocked && timestamp > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                    : null,
                ObjectiveProgresses = objProgresses
            };
        }

        public AchievementStatus[] GetAll()
        {
            return _definitions.Keys
                .Select(BuildStatus)
                .ToArray();
        }

        public AchievementStatus[] GetByGroup(GroupId groupId)
        {
            return _definitions.Values
                .Where(d => d.Group.Equals(groupId) && !d.IsHidden)
                .Select(d => BuildStatus(d.Id))
                .ToArray();
        }

        public AchievementStatus[] GetAvailable()
        {
            return _definitions.Values
                .Where(d => !d.IsHidden && !_unlocked.ContainsKey(d.Id) && _available.Contains(d.Id))
                .Select(d => BuildStatus(d.Id))
                .ToArray();
        }

        public AchievementStatus[] GetAchieved()
        {
            return _definitions.Keys
                .Where(_unlocked.ContainsKey)
                .Select(BuildStatus)
                .ToArray();
        }

        public AchievementStatus[] GetPartiallyAchieved()
        {
            return _definitions.Values
                .Where(d => !d.IsHidden && _available.Contains(d.Id) && !_unlocked.ContainsKey(d.Id))
                .Select(d => BuildStatus(d.Id))
                .Where(s => s.ObjectiveProgresses.Any(op => op.Progress.Current > 0))
                .ToArray();
        }

        public AchievementStatus GetStatus(AchievementId achievementId) => BuildStatus(achievementId);

        public GroupId[] GetGroups()
        {
            return _definitions.Values
                .Select(d => d.Group)
                .Where(g => !g.IsNone)
                .Distinct()
                .ToArray();
        }

        public void ReconcileSnapshot(AchievementStatesSnapshot snapshot)
        {
            if (snapshot?.Entries == null) return;

            foreach (var entry in snapshot.Entries)
            {
                if (entry == null || !entry.IsUnlocked || string.IsNullOrEmpty(entry.AchievementId)) continue;

                var achId = new AchievementId(entry.AchievementId);
                if (_definitions.ContainsKey(achId))
                    _unlocked[achId] = entry.UnlockedTimestamp;
            }
        }
    }
}
