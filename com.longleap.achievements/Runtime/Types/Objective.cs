using LongLeap.Achievements;

namespace LongLeap.Achievements
{
    public sealed class Objective
    {
        public ObjectiveId Id { get; init; }

        public string StatPath { get; init; }

        public int StatTarget { get; init; }

        public AchievementId[] Required { get; init; }
    }
}
