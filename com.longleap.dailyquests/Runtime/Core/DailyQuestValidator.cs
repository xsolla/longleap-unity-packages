using System;
using System.Collections.Generic;
using LongLeap.Stats;

namespace LongLeap.DailyQuests
{
    public sealed class DailyQuestValidationResult
    {
        public bool IsValid { get; }
        public string[] Errors { get; }
        public string[] Warnings { get; }

        public DailyQuestValidationResult(string[] errors, string[] warnings)
        {
            Errors = errors ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
            IsValid = Errors.Length == 0;
        }
    }

    /// <summary>
    /// Config validation. An error in one quest does not break the whole pool: the
    /// service just never issues that quest (<see cref="IsPlayable"/>), the rest work.
    /// </summary>
    public static class DailyQuestValidator
    {
        public static DailyQuestValidationResult Validate(DailyQuestConfig config)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (config == null)
            {
                errors.Add("Config is null.");
                return new DailyQuestValidationResult(errors.ToArray(), warnings.ToArray());
            }

            var settings = config.Settings ?? new DailyQuestSettings();
            if (settings.DailyCount <= 0)
                warnings.Add($"DailyCount is {settings.DailyCount} — no quests will be issued.");
            if (settings.ResetHourUtc < 0 || settings.ResetHourUtc > 23)
                warnings.Add($"ResetHourUtc {settings.ResetHourUtc} is outside 0..23 and will be wrapped.");

            var quests = config.Quests ?? Array.Empty<QuestDefinition>();
            var seen = new HashSet<QuestId>();
            var pickable = new List<QuestDefinition>();

            for (int i = 0; i < quests.Length; i++)
            {
                var def = quests[i];
                if (def == null)
                {
                    errors.Add($"Quest at index {i} is null.");
                    continue;
                }

                bool first = def.Id.IsNone || seen.Add(def.Id);
                if (!first)
                    errors.Add($"Duplicate quest ID '{def.Id}' at index {i} — only the first one is used.");

                CollectQuestErrors(def, errors);
                CollectQuestWarnings(def, warnings);

                if (first && IsPlayable(def) && def.Weight > 0)
                    pickable.Add(def);
            }

            if (settings.DailyCount > 0 && pickable.Count < settings.DailyCount)
                warnings.Add($"Only {pickable.Count} pickable quest(s) for DailyCount {settings.DailyCount} — the daily set will be smaller.");

            CollectSelectionRuleWarnings(settings, pickable, warnings);

            return new DailyQuestValidationResult(errors.ToArray(), warnings.ToArray());
        }

        /// <summary>
        /// Selection rules never drop quests — unsatisfiable ones are just relaxed
        /// during the pick (<see cref="DailyQuestSelector"/>). So these are warnings only.
        /// </summary>
        private static void CollectSelectionRuleWarnings(
            DailyQuestSettings settings, List<QuestDefinition> pickable, List<string> warnings)
        {
            if (!settings.HasSelectionRules || settings.DailyCount <= 0) return;

            var slots = settings.SlotDifficulties ?? Array.Empty<int>();

            if (slots.Length > settings.DailyCount)
                warnings.Add($"SlotDifficulties has {slots.Length} slots for DailyCount {settings.DailyCount} — only the {settings.DailyCount} hardest are used.");

            foreach (var difficulty in new HashSet<int>(slots))
            {
                if (!pickable.Exists(q => q.Difficulty == difficulty))
                    warnings.Add($"SlotDifficulties wants difficulty {difficulty}, but no pickable quest has it.");
            }

            // Pool smaller than the set — already warned about above.
            if (pickable.Count >= settings.DailyCount && !DailyQuestSelector.CanSatisfy(pickable, settings))
                warnings.Add("Selection rules (uniqueCategories / hardestLast / slotDifficulties) cannot be met by the pool — " +
                             "the set will be picked with relaxed rules: categories first, then difficulty.");
        }

        /// <summary>A quest without errors — it can be issued to the player.</summary>
        public static bool IsPlayable(QuestDefinition def)
        {
            if (def == null) return false;
            var errors = new List<string>();
            CollectQuestErrors(def, errors);
            return errors.Count == 0;
        }

        private static void CollectQuestErrors(QuestDefinition def, List<string> errors)
        {
            if (def.Id.IsNone)
            {
                errors.Add("Quest has no ID.");
                return;
            }

            if (def.Weight < 0)
                errors.Add($"Quest '{def.Id}' has negative weight {def.Weight}.");

            var objectives = def.Objectives ?? Array.Empty<QuestObjective>();
            if (objectives.Length == 0)
                errors.Add($"Quest '{def.Id}' has no objectives.");

            var objectiveIds = new HashSet<QuestObjectiveId>();
            foreach (var obj in objectives)
            {
                if (obj == null)
                {
                    errors.Add($"Quest '{def.Id}' has a null objective.");
                    continue;
                }

                if (obj.Id.IsNone)
                    errors.Add($"Quest '{def.Id}' has an objective without ID.");
                else if (!objectiveIds.Add(obj.Id))
                    errors.Add($"Quest '{def.Id}' has duplicate objective ID '{obj.Id}'.");

                if (string.IsNullOrWhiteSpace(obj.StatPath))
                    errors.Add($"Objective '{obj.Id}' in quest '{def.Id}' has no StatPath.");
                else if (!IsValidStatPath(obj.StatPath))
                    errors.Add($"Objective '{obj.Id}' in quest '{def.Id}' has malformed StatPath '{obj.StatPath}'.");

                if (obj.StatTarget <= 0)
                    errors.Add($"Objective '{obj.Id}' in quest '{def.Id}' has StatTarget <= 0.");
            }

            foreach (var reward in def.Rewards ?? Array.Empty<QuestReward>())
            {
                if (reward == null)
                {
                    errors.Add($"Quest '{def.Id}' has a null reward.");
                    continue;
                }

                if (reward.Key.IsNone)
                    errors.Add($"Quest '{def.Id}' has a reward without key.");
                if (reward.Amount <= 0)
                    errors.Add($"Quest '{def.Id}' has reward '{reward.Key}' with amount <= 0.");
            }
        }

        private static void CollectQuestWarnings(QuestDefinition def, List<string> warnings)
        {
            if (def.Id.IsNone) return;

            if (def.Weight == 0)
                warnings.Add($"Quest '{def.Id}' has weight 0 and will never be picked.");

            if (def.Rewards == null || def.Rewards.Length == 0)
                warnings.Add($"Quest '{def.Id}' has no rewards.");
        }

        private static bool IsValidStatPath(string path)
        {
            try
            {
                _ = new StatPath(path);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
