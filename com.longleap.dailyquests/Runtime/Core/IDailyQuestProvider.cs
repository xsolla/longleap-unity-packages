using System.Threading.Tasks;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Откуда берётся пул квестов и настройки. Готовые реализации:
    /// <see cref="StreamingAssetsDailyQuestProvider"/> (JSON рядом с билдом) и
    /// <see cref="JsonDailyQuestProvider"/> (JSON из строки — remote config, тесты).
    /// </summary>
    public interface IDailyQuestProvider
    {
        Task<DailyQuestConfig> LoadAsync();
    }
}
