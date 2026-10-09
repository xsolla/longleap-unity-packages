using System.Threading.Tasks;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Откуда берётся пул квестов и настройки. Готовые реализации:
    /// <see cref="ResourcesDailyQuestProvider"/> (JSON в Resources внутри билда) и
    /// <see cref="JsonDailyQuestProvider"/> (JSON из строки — remote config, тесты).
    /// </summary>
    public interface IDailyQuestProvider
    {
        Task<DailyQuestConfig> LoadAsync();
    }
}
