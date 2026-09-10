namespace LongLeap.Achievements
{
    public sealed class AchievementDefinition
    {
        public AchievementId Id { get; init; }
        public GroupId Group { get; init; }
        public bool IsHidden { get; init; }
        public AchievementId[] Prerequisites { get; init; }
        public Objective[] Objectives { get; init; }
    }
}
