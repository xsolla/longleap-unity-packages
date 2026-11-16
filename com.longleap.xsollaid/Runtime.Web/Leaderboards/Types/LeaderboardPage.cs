using System.Collections.Generic;

namespace LongLeap.XsollaId.Leaderboards
{
    public sealed class LeaderboardPage
    {
        public IReadOnlyList<LeaderboardEntry> Entries { get; }
        public object Cursor { get; }
        public bool HasMore { get; }

        public LeaderboardPage(IReadOnlyList<LeaderboardEntry> entries, object cursor, bool hasMore)
        {
            Entries = entries;
            Cursor = cursor;
            HasMore = hasMore;
        }

        public static LeaderboardPage Empty() =>
            new LeaderboardPage(new List<LeaderboardEntry>(), null, false);
    }
}
