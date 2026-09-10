namespace LongLeap.Core.Leaderboards
{
    public readonly struct BoardDefinition
    {
        public BoardId Id { get; }
        public SortDirection Sort { get; }

        public BoardDefinition(BoardId id, SortDirection sort)
        {
            Id = id;
            Sort = sort;
        }
    }
}
