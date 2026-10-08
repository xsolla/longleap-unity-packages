using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Квесты из готовой строки JSON: remote config, TextAsset, тесты.
    /// Битый JSON — пустой пул и ошибка в лог, а не исключение в загрузке.
    /// </summary>
    public sealed class JsonDailyQuestProvider : IDailyQuestProvider
    {
        private readonly string _json;

        public JsonDailyQuestProvider(string json)
        {
            _json = json;
        }

        public Task<DailyQuestConfig> LoadAsync() => Task.FromResult(ParseSafe(_json, "string"));

        internal static DailyQuestConfig ParseSafe(string json, string source)
        {
            try
            {
                return DailyQuestJson.Parse(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"DailyQuests: не удалось разобрать JSON ({source}): {e.Message}");
                return new DailyQuestConfig();
            }
        }
    }
}
