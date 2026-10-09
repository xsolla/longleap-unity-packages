using System;
using System.Threading.Tasks;
using LongLeap.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>Состояние квестов одной JSON-строкой в <see cref="IPersistentStorage"/>.</summary>
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

        public async Task<DailyQuestsState> LoadAsync()
        {
            string json;
            try
            {
                json = await _storage.LoadAsync(_storageKey);
            }
            catch (Exception e)
            {
                Debug.LogError($"JsonDailyQuestStateStore: хранилище не ответило — {e.Message}");
                return null;
            }

            return Parse(json);
        }

        public Task SaveAsync(DailyQuestsState state) =>
            _storage.SaveAsync(_storageKey, JsonConvert.SerializeObject(state));

        /// <summary>
        /// Разбор сохранения. Пусто — чистое состояние: сейва ещё не было.
        /// Битое или версия новее нашей — <see cref="DailyQuestsState.Unreadable"/>:
        /// что сегодня уже забрано, неизвестно, и сервис не выдаёт набор до
        /// следующего сброса.
        /// </summary>
        public static DailyQuestsState Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return DailyQuestsState.Empty();

            try
            {
                var state = JsonConvert.DeserializeObject<DailyQuestsState>(json);
                if (state == null)
                    return DailyQuestsState.Empty();

                if (state.Version > DailyQuestsState.CurrentVersion)
                {
                    Debug.LogWarning($"JsonDailyQuestStateStore: сохранение версии {state.Version}, а мы умеем {DailyQuestsState.CurrentVersion}.");
                    return DailyQuestsState.Unreadable();
                }

                state.Active ??= Array.Empty<ActiveQuestState>();
                return state;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"JsonDailyQuestStateStore: сохранение не читается ({e.Message}).");
                return DailyQuestsState.Unreadable();
            }
        }
    }
}
