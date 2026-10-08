using System;

namespace LongLeap.DailyQuests
{
    /// <summary>Всё, что отдаёт <see cref="IDailyQuestProvider"/>: настройки и пул квестов.</summary>
    public sealed class DailyQuestConfig
    {
        public DailyQuestSettings Settings { get; init; } = new DailyQuestSettings();

        public QuestDefinition[] Quests { get; init; } = Array.Empty<QuestDefinition>();
    }
}
