using System.Threading.Tasks;

namespace LongLeap.Achievements
{
    /// <summary>
    /// Loads and saves achievement definitions from a persistent source.
    /// </summary>
    public interface IAchievementProvider
    {
        /// <summary>
        /// Asynchronously loads all achievement definitions from the configured source.
        /// </summary>
        Task<AchievementDefinition[]> LoadAsync();


    }
}
