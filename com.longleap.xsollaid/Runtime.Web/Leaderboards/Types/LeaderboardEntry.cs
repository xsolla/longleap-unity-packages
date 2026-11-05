namespace LongLeap.XsollaId.Leaderboards
{
    public readonly struct LeaderboardEntry
    {
        public string Uid { get; }
        public string DisplayName { get; }
        public long Score { get; }
        public int Rank { get; }
        public long Timestamp { get; }

        public LeaderboardEntry(string uid, string displayName, long score, int rank, long timestamp)
        {
            Uid = uid;
            DisplayName = displayName;
            Score = score;
            Rank = rank;
            Timestamp = timestamp;
        }
    }
}
