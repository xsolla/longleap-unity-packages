using System.Threading.Tasks;

namespace LongLeap.DailyRewards
{
    public interface IDailyRewardsStateStore
    {
        /// <summary>
        /// The saved state and how reading it went (<see cref="DailyRewardsStateLoadStatus"/>).
        /// No save — <c>Empty</c>; cannot be read — <c>Unreadable</c>; the store
        /// did not answer — <c>Unavailable</c>. The last two must never be turned into <c>Empty</c>:
        /// an already claimed day would be claimed a second time.
        /// </summary>
        Task<DailyRewardsStateLoadResult> LoadAsync();

        Task SaveAsync(DailyRewardsState state);
    }
}
