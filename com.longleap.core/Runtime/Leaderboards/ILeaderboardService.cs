using System;
using System.Threading.Tasks;

namespace LongLeap.Core.Leaderboards
{
    public interface ILeaderboardService : IDisposable
    {
        ILeaderboard GetBoard(BoardId boardId, SortDirection sort);
        event Action<BoardId, int> OnTopRankAchieved;
        Task FlushAsync();

        // Uid, под которым сервис отправляет и запрашивает счёт. UI сравнивает с
        // ним Uid строк, чтобы найти себя в списке — брать его откуда-то ещё
        // (SystemInfo и т.п.) нельзя, совпадения не будет.
        Task<string> GetPlayerUidAsync();

        // Имя, под которым уходит счёт — нужно UI для строки игрока, если её
        // пришлось построить самому (окружение не пришло).
        string GetPlayerDisplayName();
    }
}
