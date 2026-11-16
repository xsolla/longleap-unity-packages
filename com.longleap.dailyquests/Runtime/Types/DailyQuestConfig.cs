using System;

namespace LongLeap.DailyQuests
{
    /// <summary>Everything <see cref="IDailyQuestProvider"/> returns: settings and the quest pool.</summary>
    public sealed class DailyQuestConfig
    {
        public DailyQuestSettings Settings { get; init; } = new DailyQuestSettings();

        public QuestDefinition[] Quests { get; init; } = Array.Empty<QuestDefinition>();
    }
}
