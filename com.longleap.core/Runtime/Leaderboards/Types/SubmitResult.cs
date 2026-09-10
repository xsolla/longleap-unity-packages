namespace LongLeap.Core.Leaderboards
{
    public readonly struct SubmitResult
    {
        public bool IsAccepted { get; }
        public bool IsNewBest { get; }
        public long Score { get; }

        public SubmitResult(bool isAccepted, bool isNewBest, long score)
        {
            IsAccepted = isAccepted;
            IsNewBest = isNewBest;
            Score = score;
        }
    }
}
