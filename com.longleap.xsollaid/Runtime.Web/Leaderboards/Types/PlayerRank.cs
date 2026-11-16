namespace LongLeap.XsollaId.Leaderboards
{
    public readonly struct PlayerRank
    {
        public int? Rank { get; }
        public long? Score { get; }
        public int TotalPlayers { get; }

        public PlayerRank(int? rank, long? score, int totalPlayers)
        {
            Rank = rank;
            Score = score;
            TotalPlayers = totalPlayers;
        }
    }
}
