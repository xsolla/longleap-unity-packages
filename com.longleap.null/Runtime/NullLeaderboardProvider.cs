using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.Core.Leaderboards;

namespace LongLeap.Null
{
    public sealed class NullLeaderboardProvider : ILeaderboardProvider
    {
        public Task<bool> SubmitScoreAsync(BoardId boardId, string uid, long score, string displayName, SortDirection sort)
        {
            return Task.FromResult(true);
        }

        public Task<LeaderboardPage> GetTopAsync(BoardId boardId, SortDirection sort, int count)
        {
            return Task.FromResult(LeaderboardPage.Empty());
        }

        public Task<PlayerRank> GetPlayerRankAsync(BoardId boardId, SortDirection sort, string uid)
        {
            return Task.FromResult(new PlayerRank(null, null, 0));
        }

        public Task<LeaderboardPage> GetSurroundingsAsync(BoardId boardId, SortDirection sort, string uid, int radius)
        {
            return Task.FromResult(LeaderboardPage.Empty());
        }

        public void Dispose()
        {
            // Null-реализация — ресурсов для освобождения нет
        }
    }
}
