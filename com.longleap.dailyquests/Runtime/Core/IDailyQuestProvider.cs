using System.Threading.Tasks;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Where the quest pool and settings come from. Built-in implementations:
    /// <see cref="ResourcesDailyQuestProvider"/> (JSON in Resources inside the build) and
    /// <see cref="JsonDailyQuestProvider"/> (JSON from a string — remote config, tests).
    /// </summary>
    public interface IDailyQuestProvider
    {
        Task<DailyQuestConfig> LoadAsync();
    }
}
