using System;

namespace LongLeap.Achievements
{
    [Serializable]
    public sealed class AchievementStateEntry
    {
        public string AchievementId;
        public bool IsUnlocked;
        public long UnlockedTimestamp;
    }
}
