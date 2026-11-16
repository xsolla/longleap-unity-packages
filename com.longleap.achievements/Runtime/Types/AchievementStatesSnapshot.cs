using System;

namespace LongLeap.Achievements
{
    [Serializable]
    public sealed class AchievementStatesSnapshot
    {
        public int Version;
        public AchievementStateEntry[] Entries;
    }
}
