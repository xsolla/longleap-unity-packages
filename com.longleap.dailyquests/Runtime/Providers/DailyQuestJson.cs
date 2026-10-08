using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Разбор JSON с квестами. Формат:
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
    /// Необязательные поля: <c>settings</c> целиком (и каждое поле в нём),
    /// <c>weight</c> (1), <c>objectives[].id</c> («o0», «o1», …),
    /// <c>objectives[].mode</c> («delta» | «absolute», по умолчанию delta).
    /// </summary>
    public static class DailyQuestJson
    {
        /// <summary>Бросает <see cref="JsonException"/> на битом JSON.</summary>
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
                    ResetHourUtc = dto.settings?.resetHourUtc ?? 0
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
                    Debug.LogWarning($"DailyQuestJson: квест '{questId}' — неизвестный mode '{mode}', считаю как delta.");
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
        }

        private sealed class QuestDto
        {
            public string id;
            public int? weight;
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
