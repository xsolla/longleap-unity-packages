using System;
using System.Threading.Tasks;

namespace LongLeap.XsollaId.Leaderboards
{
    public interface ILeaderboardService : IDisposable
    {
        ILeaderboard GetBoard(BoardId boardId, SortDirection sort);
        event Action<BoardId, int> OnTopRankAchieved;
        Task FlushAsync();

        // The uid the service submits and queries scores with. UI compares row
        // uids against it to find the player in a list — taking a uid from
        // anywhere else (SystemInfo etc.) will not match.
        Task<string> GetPlayerUidAsync();

        // The name scores are submitted under. UI needs it to build the player's
        // own row when the surroundings did not arrive.
        string GetPlayerDisplayName();
    }
}
