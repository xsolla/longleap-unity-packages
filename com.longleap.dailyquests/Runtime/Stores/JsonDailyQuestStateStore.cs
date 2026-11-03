using System;
using System.Threading.Tasks;
using LongLeap.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>Quest state as a single JSON string in <see cref="IPersistentStorage"/>.</summary>
    public sealed class JsonDailyQuestStateStore : IDailyQuestStateStore
    {
        public const string DefaultStorageKey = "daily_quests_state";

        private readonly IPersistentStorage _storage;
        private readonly string _storageKey;

        public JsonDailyQuestStateStore(IPersistentStorage storage, string storageKey = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _storageKey = storageKey ?? DefaultStorageKey;
        }

        public async Task<DailyQuestStateLoadResult> LoadAsync()
        {
            string json;
            try
            {
                json = await _storage.LoadAsync(_storageKey);
            }
            catch (Exception e)
            {
                Debug.LogError($"JsonDailyQuestStateStore: storage did not answer — {e.Message}");
                return DailyQuestStateLoadResult.Unavailable();
            }

            return Parse(json);
        }

        public Task SaveAsync(DailyQuestsState state) =>
            _storage.SaveAsync(_storageKey, JsonConvert.SerializeObject(state));

        /// <summary>
        /// Parses a save. Empty — <see cref="DailyQuestStateLoadStatus.Empty"/>:
        /// there was no save yet. Corrupted or newer than our version —
        /// <see cref="DailyQuestStateLoadStatus.Unreadable"/>: what has already been claimed
        /// today is unknown, and the service issues no set until the next reset.
        /// </summary>
        public static DailyQuestStateLoadResult Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return DailyQuestStateLoadResult.Empty();

            try
            {
                var state = JsonConvert.DeserializeObject<DailyQuestsState>(json);
                if (state == null)
                    return DailyQuestStateLoadResult.Empty();

                if (state.Version > DailyQuestsState.CurrentVersion)
                {
                    Debug.LogWarning($"JsonDailyQuestStateStore: save version {state.Version} is newer than supported {DailyQuestsState.CurrentVersion}.");
                    return DailyQuestStateLoadResult.Unreadable();
                }

                state.Active ??= Array.Empty<ActiveQuestState>();
                return DailyQuestStateLoadResult.Loaded(state);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"JsonDailyQuestStateStore: save cannot be read ({e.Message}).");
                return DailyQuestStateLoadResult.Unreadable();
            }
        }
    }
}
