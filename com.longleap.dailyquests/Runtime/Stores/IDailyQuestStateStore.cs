using System.Threading.Tasks;

namespace LongLeap.DailyQuests
{
    public interface IDailyQuestStateStore
    {
        /// <summary>
        /// The saved state and how reading it went
        /// (<see cref="DailyQuestStateLoadStatus"/>). No save — <c>Empty</c>;
        /// cannot be read — <c>Unreadable</c>; the store did not answer — <c>Unavailable</c>.
        /// The last two must never be turned into <c>Empty</c>: a claimed reward could
        /// be claimed a second time.
        /// </summary>
        Task<DailyQuestStateLoadResult> LoadAsync();

        Task SaveAsync(DailyQuestsState state);
    }
}
