using System.Threading.Tasks;

namespace LongLeap.DailyRewards
{
    public interface IDailyRewardsStateStore
    {
        Task<DailyRewardsState> LoadAsync();
        Task SaveAsync(DailyRewardsState state);
    }
}