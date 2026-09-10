using System;
using System.Collections.Generic;
using System.Linq;

namespace LongLeap.Achievements
{
    public sealed class ValidationResult
    {
        public bool IsValid { get; }
        public string[] Errors { get; }
        public string[] Warnings { get; }

        public ValidationResult(string[] errors, string[] warnings)
        {
            Errors = errors ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
            IsValid = Errors.Length == 0;
        }
    }

    public static class AchievementValidator
    {
        public static ValidationResult Validate(AchievementDefinition[] definitions)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (definitions == null || definitions.Length == 0)
                return new ValidationResult(errors.ToArray(), warnings.ToArray());

            var definedIds = new HashSet<AchievementId>();

            CheckDuplicateAchievementIds(definitions, errors);
            CollectDefinedIds(definitions, definedIds);

            foreach (var def in definitions)
            {
                CheckDuplicateObjectiveIds(def, errors);
                CheckObjectiveContent(def, errors);
                CheckDanglingPrerequisites(def, definedIds, warnings);
                CheckEmptyAchievement(def, warnings);
            }

            CheckPrerequisiteCycles(definitions, definedIds, errors);

            return new ValidationResult(errors.ToArray(), warnings.ToArray());
        }

        private static void CheckDuplicateAchievementIds(AchievementDefinition[] definitions, List<string> errors)
        {
            var seen = new Dictionary<AchievementId, List<int>>();
            for (int i = 0; i < definitions.Length; i++)
            {
                var id = definitions[i].Id;
                if (!seen.ContainsKey(id))
                    seen[id] = new List<int>();
                seen[id].Add(i);
            }

            foreach (var kvp in seen)
            {
                if (kvp.Value.Count > 1)
                {
                    var indices = string.Join(", ", kvp.Value);
                    errors.Add($"Duplicate achievement ID '{kvp.Key}' found at indices [{indices}].");
                }
            }
        }

        private static void CollectDefinedIds(AchievementDefinition[] definitions, HashSet<AchievementId> definedIds)
        {
            foreach (var def in definitions)
            {
                if (!def.Id.IsNone)
                    definedIds.Add(def.Id);
            }
        }

        private static void CheckDuplicateObjectiveIds(AchievementDefinition def, List<string> errors)
        {
            var seen = new HashSet<ObjectiveId>();
            foreach (var obj in def.Objectives)
            {
                if (!seen.Add(obj.Id))
                    errors.Add($"Achievement '{def.Id}' has duplicate objective ID '{obj.Id}'.");
            }
        }

        private static void CheckObjectiveContent(AchievementDefinition def, List<string> errors)
        {
            foreach (var obj in def.Objectives ?? Array.Empty<Objective>())
            {
                bool hasStat = !string.IsNullOrEmpty(obj.StatPath);
                bool hasRequired = obj.Required != null && obj.Required.Length > 0;

                if (!hasStat && !hasRequired)
                    errors.Add($"Objective '{obj.Id}' in achievement '{def.Id}' has no StatPath and no Required.");

                if (hasStat && hasRequired)
                    errors.Add($"Objective '{obj.Id}' in achievement '{def.Id}' has both StatPath and Required — must be one.");

                if (hasStat && obj.StatTarget <= 0)
                    errors.Add($"Objective '{obj.Id}' in achievement '{def.Id}' has StatPath but StatTarget is <= 0.");
            }
        }

        private static void CheckDanglingPrerequisites(AchievementDefinition def, HashSet<AchievementId> definedIds, List<string> warnings)
        {
            foreach (var prereq in def.Prerequisites ?? Array.Empty<AchievementId>())
            {
                if (!prereq.IsNone && !definedIds.Contains(prereq))
                    warnings.Add($"Achievement '{def.Id}' has prerequisite '{prereq}' which is not defined.");
            }
        }

        private static void CheckEmptyAchievement(AchievementDefinition def, List<string> warnings)
        {
            var hasObjectives = def.Objectives != null && def.Objectives.Length > 0;
            var hasPrerequisites = def.Prerequisites != null && def.Prerequisites.Length > 0;

            if (!hasObjectives && !hasPrerequisites)
                warnings.Add($"Achievement '{def.Id}' has 0 objectives and 0 prerequisites.");
        }

        private static void CheckPrerequisiteCycles(AchievementDefinition[] definitions, HashSet<AchievementId> definedIds, List<string> errors)
        {
            var idToIndex = new Dictionary<AchievementId, int>();
            for (int i = 0; i < definitions.Length; i++)
            {
                if (!definitions[i].Id.IsNone)
                    idToIndex[definitions[i].Id] = i;
            }

            int n = definitions.Length;
            var adjacency = new List<int>[n];
            var inDegree = new int[n];

            for (int i = 0; i < n; i++)
                adjacency[i] = new List<int>();

            for (int i = 0; i < n; i++)
            {
                var prereqs = definitions[i].Prerequisites ?? Array.Empty<AchievementId>();
                foreach (var prereq in prereqs)
                {
                    if (prereq.IsNone) continue;
                    if (idToIndex.TryGetValue(prereq, out int prereqIndex))
                    {
                        adjacency[prereqIndex].Add(i);
                        inDegree[i]++;
                    }
                }
            }

            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
                if (inDegree[i] == 0)
                    queue.Enqueue(i);

            int processed = 0;
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                processed++;
                foreach (var dependent in adjacency[node])
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                        queue.Enqueue(dependent);
                }
            }

            if (processed != n)
            {
                var cycleNodes = new List<AchievementId>();
                for (int i = 0; i < n; i++)
                    if (inDegree[i] > 0)
                        cycleNodes.Add(definitions[i].Id);

                var ids = string.Join(", ", cycleNodes.Select(id => $"'{id}'"));
                errors.Add($"Circular prerequisite dependency detected involving achievements: {ids}.");
            }
        }
    }
}
