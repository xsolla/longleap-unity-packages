using System;
using System.Threading.Tasks;

namespace LongLeap.Core.Leaderboards
{
    public interface ILeaderboardProvider : IDisposable
    {
        Task<bool> SubmitScoreAsync(BoardId boardId, string uid, long score, string displayName, SortDirection sort);
        Task<LeaderboardPage> GetTopAsync(BoardId boardId, SortDirection sort, int count);
        Task<PlayerRank> GetPlayerRankAsync(BoardId boardId, SortDirection sort, string uid);
        Task<LeaderboardPage> GetSurroundingsAsync(BoardId boardId, SortDirection sort, string uid, int radius);
        void Dispose();
    }
}
