using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Parses the quests JSON. Format:
    /// <code>
    /// {
    ///   "settings": { "dailyCount": 3, "resetHourUtc": 0 },
    ///   "quests": [
    ///     {
    ///       "id": "grind_50",
    ///       "weight": 1,
    ///       "objectives": [ { "statPath": "grind", "statTarget": 50 } ],
    ///       "rewards": [ { "key": "soft", "amount": 5 } ]
    ///     }
    ///   ]
    /// }
    /// </code>
    /// Optional fields: <c>settings</c> as a whole (and every field in it:
    /// <c>uniqueCategories</c>, <c>hardestLast</c>, <c>slotDifficulties</c> — selection
    /// rules, see <see cref="DailyQuestSelector"/>), <c>weight</c> (1),
    /// <c>categories</c> ([]), <c>difficulty</c> (0), <c>objectives[].id</c>
    /// ("o0", "o1", …), <c>objectives[].mode</c> ("delta" | "absolute", delta by default).
    /// </summary>
    public static class DailyQuestJson
    {
        /// <summary>Throws <see cref="JsonException"/> on malformed JSON.</summary>
        public static DailyQuestConfig Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new DailyQuestConfig();

            var dto = JsonConvert.DeserializeObject<FileDto>(json);
            if (dto == null)
                return new DailyQuestConfig();

            return new DailyQuestConfig
            {
                Settings = new DailyQuestSettings
                {
                    DailyCount = dto.settings?.dailyCount ?? DailyQuestSettings.DefaultDailyCount,
                    ResetHourUtc = dto.settings?.resetHourUtc ?? 0,
                    UniqueCategories = dto.settings?.uniqueCategories ?? false,
                    HardestLast = dto.settings?.hardestLast ?? false,
                    SlotDifficulties = dto.settings?.slotDifficulties ?? Array.Empty<int>()
                },
                Quests = dto.quests?
                    .Where(q => q != null)
                    .Select(MapQuest)
                    .ToArray() ?? Array.Empty<QuestDefinition>()
            };
        }

        private static QuestDefinition MapQuest(QuestDto q)
        {
            return new QuestDefinition
            {
                Id = string.IsNullOrEmpty(q.id) ? QuestId.None : new QuestId(q.id),
                Weight = q.weight ?? 1,
                Categories = q.categories?
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim())
                    .ToArray() ?? Array.Empty<string>(),
                Difficulty = q.difficulty ?? 0,
                Objectives = q.objectives?
                    .Select((o, i) => o == null ? null : MapObjective(q.id, o, i))
                    .ToArray() ?? Array.Empty<QuestObjective>(),
                Rewards = q.rewards?
                    .Select(r => r == null ? null : new QuestReward
                    {
                        Key = string.IsNullOrEmpty(r.key) ? QuestRewardKey.None : new QuestRewardKey(r.key),
                        Amount = r.amount
                    })
                    .ToArray() ?? Array.Empty<QuestReward>()
            };
        }

        private static QuestObjective MapObjective(string questId, ObjectiveDto o, int index)
        {
            return new QuestObjective
            {
                Id = new QuestObjectiveId(string.IsNullOrEmpty(o.id) ? "o" + index : o.id),
                StatPath = o.statPath,
                StatTarget = o.statTarget,
                Mode = ParseMode(questId, o.mode)
            };
        }

        private static QuestProgressMode ParseMode(string questId, string mode)
        {
            if (string.IsNullOrEmpty(mode)) return QuestProgressMode.Delta;

            switch (mode.Trim().ToLowerInvariant())
            {
                case "delta": return QuestProgressMode.Delta;
                case "absolute": return QuestProgressMode.Absolute;
                default:
                    Debug.LogWarning($"DailyQuestJson: quest '{questId}' has unknown mode '{mode}', treating it as delta.");
                    return QuestProgressMode.Delta;
            }
        }

#pragma warning disable CS0649
        private sealed class FileDto
        {
            public SettingsDto settings;
            public QuestDto[] quests;
        }

        private sealed class SettingsDto
        {
            public int? dailyCount;
            public int? resetHourUtc;
            public bool? uniqueCategories;
            public bool? hardestLast;
            public int[] slotDifficulties;
        }

        private sealed class QuestDto
        {
            public string id;
            public int? weight;
            public string[] categories;
            public int? difficulty;
            public ObjectiveDto[] objectives;
            public RewardDto[] rewards;
        }

        private sealed class ObjectiveDto
        {
            public string id;
            public string statPath;
            public int statTarget;
            public string mode;
        }

        private sealed class RewardDto
        {
            public string key;
            public int amount;
        }
#pragma warning restore CS0649
    }
}
