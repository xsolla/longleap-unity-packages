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
    /// Проверка конфига. Ошибка в квесте не роняет весь пул: сервис просто не
    /// выдаёт такой квест (<see cref="IsPlayable"/>), а остальные работают.
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
            int pickable = 0;

            for (int i = 0; i < quests.Length; i++)
            {
                var def = quests[i];
                if (def == null)
                {
                    errors.Add($"Quest at index {i} is null.");
                    continue;
                }

                if (!def.Id.IsNone && !seen.Add(def.Id))
                    errors.Add($"Duplicate quest ID '{def.Id}' at index {i} — only the first one is used.");

                CollectQuestErrors(def, errors);
                CollectQuestWarnings(def, warnings);

                if (IsPlayable(def) && def.Weight > 0)
                    pickable++;
            }

            if (settings.DailyCount > 0 && pickable < settings.DailyCount)
                warnings.Add($"Only {pickable} pickable quest(s) for DailyCount {settings.DailyCount} — the daily set will be smaller.");

            return new DailyQuestValidationResult(errors.ToArray(), warnings.ToArray());
        }

        /// <summary>Квест без ошибок — его можно выдавать игроку.</summary>
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
