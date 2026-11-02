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

    // Works on raw provider data, so every check tolerates nulls: a null entry
    // or array is reported, never thrown on.
    public static class AchievementValidator
    {
        public static ValidationResult Validate(AchievementDefinition[] definitions)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (definitions == null || definitions.Length == 0)
                return new ValidationResult(errors.ToArray(), warnings.ToArray());

            CheckMissingDefinitionsAndIds(definitions, errors);
            CheckDuplicateAchievementIds(definitions, errors);

            var definedIds = new HashSet<AchievementId>(
                definitions.Where(d => d != null && !d.Id.IsNone).Select(d => d.Id));

            foreach (var def in definitions)
            {
                if (def == null || def.Id.IsNone) continue;

                CheckDuplicateObjectiveIds(def, errors);
                CheckObjectiveContent(def, errors);
                CheckReferences(def, "prerequisite", def.Prerequisites, definedIds, errors, warnings);
                foreach (var obj in def.Objectives ?? Array.Empty<Objective>())
                {
                    if (obj != null)
                        CheckReferences(def, $"objective '{obj.Id}' required achievement", obj.Required, definedIds, errors, warnings);
                }
                CheckEmptyAchievement(def, warnings);
            }

            CheckDependencyCycles(definitions, errors);

            return new ValidationResult(errors.ToArray(), warnings.ToArray());
        }

        private static void CheckMissingDefinitionsAndIds(AchievementDefinition[] definitions, List<string> errors)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                if (definitions[i] == null)
                    errors.Add($"Achievement at index {i} is null.");
                else if (definitions[i].Id.IsNone)
                    errors.Add($"Achievement at index {i} has no ID.");
            }
        }

        private static void CheckDuplicateAchievementIds(AchievementDefinition[] definitions, List<string> errors)
        {
            var seen = new Dictionary<AchievementId, List<int>>();
            for (int i = 0; i < definitions.Length; i++)
            {
                if (definitions[i] == null || definitions[i].Id.IsNone) continue;

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
                    errors.Add($"Duplicate achievement ID '{kvp.Key}' found at indices [{indices}]; only the first one is used.");
                }
            }
        }

        private static void CheckDuplicateObjectiveIds(AchievementDefinition def, List<string> errors)
        {
            var seen = new HashSet<ObjectiveId>();
            foreach (var obj in def.Objectives ?? Array.Empty<Objective>())
            {
                if (obj != null && !seen.Add(obj.Id))
                    errors.Add($"Achievement '{def.Id}' has duplicate objective ID '{obj.Id}'.");
            }
        }

        private static void CheckObjectiveContent(AchievementDefinition def, List<string> errors)
        {
            var objectives = def.Objectives ?? Array.Empty<Objective>();
            for (int i = 0; i < objectives.Length; i++)
            {
                var obj = objectives[i];
                if (obj == null)
                {
                    errors.Add($"Achievement '{def.Id}' has a null objective at index {i}.");
                    continue;
                }

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

        // A reference to itself is an error: such an achievement can never
        // unlock. A reference to an undefined achievement is a warning: it may be
        // a typo, or an achievement removed from the config on purpose — either
        // way the dependent one stays locked.
        private static void CheckReferences(AchievementDefinition def, string kind, AchievementId[] references,
            HashSet<AchievementId> definedIds, List<string> errors, List<string> warnings)
        {
            foreach (var reference in references ?? Array.Empty<AchievementId>())
            {
                if (reference.IsNone) continue;

                if (reference == def.Id)
                    errors.Add($"Achievement '{def.Id}' has itself as {kind}.");
                else if (!definedIds.Contains(reference))
                    warnings.Add($"Achievement '{def.Id}' has {kind} '{reference}' which is not defined.");
            }
        }

        private static void CheckEmptyAchievement(AchievementDefinition def, List<string> warnings)
        {
            var hasObjectives = def.Objectives != null && def.Objectives.Length > 0;
            var hasPrerequisites = def.Prerequisites != null && def.Prerequisites.Length > 0;

            if (!hasObjectives && !hasPrerequisites)
                warnings.Add($"Achievement '{def.Id}' has 0 objectives and 0 prerequisites.");
        }

        // Prerequisites and Required both mean "unlocks only after X", so a cycle
        // through any mix of them leaves every achievement in it locked forever.
        // Self-references are reported by CheckReferences and skipped here.
        private static void CheckDependencyCycles(AchievementDefinition[] definitions, List<string> errors)
        {
            // First definition wins on duplicates, the same as at load time.
            var nodes = new List<AchievementDefinition>();
            var idToIndex = new Dictionary<AchievementId, int>();
            foreach (var def in definitions)
            {
                if (def == null || def.Id.IsNone || idToIndex.ContainsKey(def.Id)) continue;
                idToIndex[def.Id] = nodes.Count;
                nodes.Add(def);
            }

            int n = nodes.Count;
            var adjacency = new List<int>[n];
            var inDegree = new int[n];

            for (int i = 0; i < n; i++)
                adjacency[i] = new List<int>();

            for (int i = 0; i < n; i++)
            {
                var dependencies = new HashSet<AchievementId>(nodes[i].Prerequisites ?? Array.Empty<AchievementId>());
                foreach (var obj in nodes[i].Objectives ?? Array.Empty<Objective>())
                    dependencies.UnionWith(obj?.Required ?? Array.Empty<AchievementId>());

                foreach (var dependency in dependencies)
                {
                    if (dependency.IsNone || dependency == nodes[i].Id) continue;
                    if (idToIndex.TryGetValue(dependency, out int dependencyIndex))
                    {
                        adjacency[dependencyIndex].Add(i);
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
                        cycleNodes.Add(nodes[i].Id);

                var ids = string.Join(", ", cycleNodes.Select(id => $"'{id}'"));
                errors.Add($"Circular dependency (Prerequisites or Required) detected involving achievements: {ids}.");
            }
        }
    }
}
