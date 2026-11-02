using System.Threading.Tasks;

namespace LongLeap.Achievements
{
    /// <summary>
    /// Supplies achievement definitions. Implemented by the game.
    /// </summary>
    public interface IAchievementProvider
    {
        /// <summary>
        /// Asynchronously loads all achievement definitions from the configured source.
        /// The result is validated and normalized by <see cref="AchievementSystem"/>:
        /// null entries and arrays are tolerated, duplicates keep the first definition.
        /// </summary>
        Task<AchievementDefinition[]> LoadAsync();
    }
}
