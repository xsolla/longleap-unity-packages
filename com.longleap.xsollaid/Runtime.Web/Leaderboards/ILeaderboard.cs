using System.Threading.Tasks;

namespace LongLeap.XsollaId.Leaderboards
{
    public interface ILeaderboard
    {
        BoardId Id { get; }
        Task<SubmitResult> SubmitScoreAsync(long score);
        Task<LeaderboardPage> GetTopAsync(int count);
        Task<PlayerRank> GetMyRankAsync();
        Task<LeaderboardPage> GetSurroundingsAsync(int radius);
    }
}
