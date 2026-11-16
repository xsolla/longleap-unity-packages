namespace LongLeap.Achievements
{
    public interface IAchievementGameBridge
    {
        void OnAvailable(AchievementId achievementId);
        void OnUnlocked(AchievementDefinition definition);
    }
}
