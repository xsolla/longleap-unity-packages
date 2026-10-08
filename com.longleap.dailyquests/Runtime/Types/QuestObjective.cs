namespace LongLeap.DailyQuests
{
    /// <summary>Одна измеримая цель квеста: стат и сколько по нему набрать.</summary>
    public sealed class QuestObjective
    {
        public QuestObjectiveId Id { get; init; }

        public string StatPath { get; init; }

        public int StatTarget { get; init; }

        public QuestProgressMode Mode { get; init; }
    }
}
