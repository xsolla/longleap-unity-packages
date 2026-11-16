using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Quests from a ready JSON string: remote config, TextAsset, tests.
    /// Malformed JSON gives an empty pool and an error in the log, not an exception during loading.
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
                Debug.LogError($"DailyQuests: failed to parse JSON ({source}): {e.Message}");
                return new DailyQuestConfig();
            }
        }
    }
}
