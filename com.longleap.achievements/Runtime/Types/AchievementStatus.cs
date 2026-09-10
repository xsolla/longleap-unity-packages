namespace LongLeap.Achievements
{
    public sealed class AchievementStatus
    {
        public AchievementDefinition Definition { get; init; }
        public bool IsAvailable { get; init; }
        public bool IsUnlocked { get; init; }
        public ObjectiveProgress[] ObjectiveProgresses { get; init; }
    }
}
