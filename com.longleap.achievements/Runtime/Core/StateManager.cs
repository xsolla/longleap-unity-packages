using System;
using System.Collections.Generic;
using System.Linq;

namespace LongLeap.Achievements
{
    public sealed class StateManager
    {
        private readonly HashSet<AchievementId> _unlocked = new();
        private readonly HashSet<AchievementId> _available = new();
        private readonly HashSet<AchievementId> _legacy = new();
        private readonly Dictionary<AchievementId, AchievementDefinition> _definitions = new();
        private readonly Dictionary<AchievementId, HashSet<AchievementId>> _objectiveProgressUnlockDeps = new();
        // Прогресс по целям со статами. Ключ — ПАРА (ачивка, цель), а не один id
        // цели: id уникален только внутри своей ачивки (ровно это и проверяет
        // AchievementValidator.CheckDuplicateObjectiveIds), а в конфиге они
        // короткие и повторяются — "k" у всех kill_*, "h" сразу у big_hit_500 и
        // heal_10. С ключом из одного id последняя записавшая ачивка затирала
        // прогресс остальных, и в окне у kill_50_grunts стояло общее число килов.
        private readonly Dictionary<(AchievementId, ObjectiveId), int> _progressOverrides = new();

        public bool IsDirty { get; private set; }
        public void MarkDirty() => IsDirty = true;
        public void ClearDirty() => IsDirty = false;
        public IReadOnlyDictionary<AchievementId, AchievementDefinition> Definitions => _definitions;

        public void LoadDefinitions(AchievementDefinition[] definitions)
        {
            _definitions.Clear();
            _objectiveProgressUnlockDeps.Clear();
            foreach (var def in definitions)
            {
                _definitions[def.Id] = def;
                foreach (var obj in def.Objectives ?? Array.Empty<Objective>())
                {
                    if (obj.Required != null && obj.Required.Length > 0)
                    {
                        foreach (var required in obj.Required)
                        {
                            if (!_objectiveProgressUnlockDeps.TryGetValue(required, out var dependents))
                            {
                                dependents = new HashSet<AchievementId>();
                                _objectiveProgressUnlockDeps[required] = dependents;
                            }
                            dependents.Add(def.Id);
                        }
                    }
                }
            }
        }

        public void SetProgressOverride(AchievementId achievementId, ObjectiveId objectiveId, int value)
        {
            _progressOverrides[(achievementId, objectiveId)] = value;
        }

        public void Clear()
        {
            _unlocked.Clear();
            _available.Clear();
            _legacy.Clear();
            _objectiveProgressUnlockDeps.Clear();
            _progressOverrides.Clear();
            IsDirty = false;
        }

        public bool IsUnlocked(AchievementId achId) => _unlocked.Contains(achId);
        public bool IsAvailable(AchievementId achId) => _available.Contains(achId);
        public bool IsLegacy(AchievementId achId) => _legacy.Contains(achId);

        public bool TryMarkUnlocked(AchievementId achId)
        {
            if (_unlocked.Contains(achId)) return false;
            _unlocked.Add(achId);
            _available.Remove(achId);
            IsDirty = true;
            return true;
        }

        public void SetAvailable(AchievementId achId) => _available.Add(achId);

        public bool IsFullyAvailable(AchievementId achId)
        {
            if (!_available.Contains(achId)) return false;
            if (_unlocked.Contains(achId)) return false;
            return true;
        }

        public void ComputeAvailability()
        {
            _available.Clear();
            foreach (var kvp in _definitions)
            {
                var id = kvp.Key;
                var def = kvp.Value;
                if (_unlocked.Contains(id)) continue;

                bool prereqsMet = true;
                if (def.Prerequisites != null)
                {
                    foreach (var prereq in def.Prerequisites)
                    {
                        if (!_unlocked.Contains(prereq))
                        {
                            prereqsMet = false;
                            break;
                        }
                    }
                }
                if (prereqsMet)
                    _available.Add(id);
            }
        }

        public (List<AchievementId> AutoUnlocks, List<AchievementId> Notifications) ComputeAutoUnlocksWithNotifications()
        {
            var autoUnlocks = new List<AchievementId>();
            var notifications = new List<AchievementId>();

            foreach (var id in _available.ToList())
            {
                if (!_definitions.TryGetValue(id, out var def)) continue;
                if (def.Objectives == null || def.Objectives.Length == 0)
                    autoUnlocks.Add(id);
                else
                    notifications.Add(id);
            }

            return (autoUnlocks, notifications);
        }

        public List<AchievementId> CheckPrerequisiteUnlockDeps(AchievementId unlockedAchievement)
        {
            var unlocked = new List<AchievementId>();
            if (!_objectiveProgressUnlockDeps.TryGetValue(unlockedAchievement, out var dependents))
                return unlocked;

            foreach (var depAchId in dependents)
            {
                if (_unlocked.Contains(depAchId)) continue;
                if (!_definitions.TryGetValue(depAchId, out var def)) continue;

                bool allRequiredUnlocked = true;
                foreach (var obj in def.Objectives ?? Array.Empty<Objective>())
                {
                    if (obj.Required == null || obj.Required.Length == 0) continue;
                    allRequiredUnlocked = obj.Required.All(r => _unlocked.Contains(r));
                    if (!allRequiredUnlocked) break;
                }

                if (allRequiredUnlocked)
                    unlocked.Add(depAchId);
            }

            return unlocked;
        }

        public AchievementStatus BuildStatus(AchievementId id)
        {
            if (_legacy.Contains(id))
            {
                return new AchievementStatus
                {
                    Definition = new AchievementDefinition
                    {
                        Id = id, Group = GroupId.None, IsHidden = false,
                        Prerequisites = Array.Empty<AchievementId>(), Objectives = Array.Empty<Objective>()
                    },
                    IsAvailable = false, IsUnlocked = true,
                    ObjectiveProgresses = Array.Empty<ObjectiveProgress>()
                };
            }

            if (!_definitions.TryGetValue(id, out var def))
                return null;

            var objProgresses = def.Objectives.Select(obj =>
            {
                bool isComplete = false;
                int current = 0;
                int target = 1;

                if (obj.Required != null && obj.Required.Length > 0)
                {
                    // Цель-«список» (all_heroes, bestiary_complete) — счётчик по
                    // самим требуемым ачивкам. Раньше здесь оставались current = 0
                    // и target = 1, то есть прогресс терялся: показать «2 из 4»
                    // потребителю было нечем, а цель с target <= 1 он и обязан
                    // считать флагом «сделал/не сделал», а не счётчиком.
                    target = obj.Required.Length;
                    current = obj.Required.Count(r => _unlocked.Contains(r));
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

            return new AchievementStatus
            {
                Definition = def,
                IsAvailable = _available.Contains(id),
                IsUnlocked = _unlocked.Contains(id),
                ObjectiveProgresses = objProgresses
            };
        }

        public AchievementStatus[] GetAll()
        {
            var results = new List<AchievementStatus>();
            foreach (var kvp in _definitions)
                results.Add(BuildStatus(kvp.Key));
            foreach (var legacyId in _legacy)
                results.Add(BuildStatus(legacyId));
            return results.ToArray();
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
                .Where(d => !d.IsHidden && !_unlocked.Contains(d.Id) && _available.Contains(d.Id))
                .Select(d => BuildStatus(d.Id))
                .ToArray();
        }

        public AchievementStatus[] GetAchieved()
        {
            var results = new List<AchievementStatus>();
            foreach (var kvp in _definitions)
                if (_unlocked.Contains(kvp.Key))
                    results.Add(BuildStatus(kvp.Key));
            foreach (var legacyId in _legacy)
                results.Add(BuildStatus(legacyId));
            return results.ToArray();
        }

        public AchievementStatus[] GetPartiallyAchieved()
        {
            return _definitions.Values
                .Where(d => !d.IsHidden && _available.Contains(d.Id) && !_unlocked.Contains(d.Id))
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
                var achId = new AchievementId(entry.AchievementId);
                if (_definitions.TryGetValue(achId, out _))
                {
                    if (entry.IsUnlocked)
                        _unlocked.Add(achId);
                }
                else if (entry.IsUnlocked)
                {
                    _legacy.Add(achId);
                }
            }
        }
    }
}
