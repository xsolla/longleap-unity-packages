using System;
using System.Threading.Tasks;
using LongLeap.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.DailyRewards
{
    /// <summary>Calendar state as a single JSON string in <see cref="IPersistentStorage"/>.</summary>
    public sealed class JsonDailyRewardsStateStore : IDailyRewardsStateStore
    {
        public const string DefaultStorageKey = "daily_rewards_state";

        private readonly IPersistentStorage _storage;
        private readonly string _storageKey;

        public JsonDailyRewardsStateStore(IPersistentStorage storage, string storageKey = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _storageKey = storageKey ?? DefaultStorageKey;
        }

        public async Task<DailyRewardsStateLoadResult> LoadAsync()
        {
            string json;
            try
            {
                json = await _storage.LoadAsync(_storageKey);
            }
            catch (Exception e)
            {
                Debug.LogError($"JsonDailyRewardsStateStore: storage did not answer — {e}");
                return DailyRewardsStateLoadResult.Unavailable();
            }

            return Parse(json);
        }

        public Task SaveAsync(DailyRewardsState state) =>
            _storage.SaveAsync(_storageKey, JsonConvert.SerializeObject(state));

        /// <summary>
        /// Parse a save. Empty — <see cref="DailyRewardsStateLoadStatus.Empty"/>:
        /// there was no save yet. Corrupted or newer than our version —
        /// <see cref="DailyRewardsStateLoadStatus.Unreadable"/>: it must not be treated as empty,
        /// otherwise a day already claimed today would be granted again. Older versions are migrated
        /// to the current one (see <see cref="Migrate"/>).
        /// </summary>
        public static DailyRewardsStateLoadResult Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return DailyRewardsStateLoadResult.Empty();

            try
            {
                var token = JToken.Parse(json);
                if (token.Type == JTokenType.Null)
                    return DailyRewardsStateLoadResult.Empty();

                if (token is not JObject obj)
                {
                    Debug.LogWarning("JsonDailyRewardsStateStore: save is unreadable (expected a JSON object).");
                    return DailyRewardsStateLoadResult.Unreadable();
                }

                // Read the version from the JSON, not from the object: the field has a default
                // (the current version), so a save without a version would pass for a new one.
                var version = obj.Value<int?>(nameof(DailyRewardsState.Version)) ?? 1;
                if (version > DailyRewardsState.CurrentVersion)
                {
                    Debug.LogWarning($"JsonDailyRewardsStateStore: save version {version} is newer than supported {DailyRewardsState.CurrentVersion}.");
                    return DailyRewardsStateLoadResult.Unreadable();
                }

                var state = obj.ToObject<DailyRewardsState>();
                if (state == null)
                    return DailyRewardsStateLoadResult.Empty();

                Migrate(obj, version, state);
                if (state.ClaimedCount < 0)
                    state.ClaimedCount = 0;

                return DailyRewardsStateLoadResult.Loaded(state);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"JsonDailyRewardsStateStore: save is unreadable ({e.Message}).");
                return DailyRewardsStateLoadResult.Unreadable();
            }
        }

        /// <summary>
        /// Migrate older formats to the current one.
        /// v1 → v2: the ClaimedDayIndices array of claimed days is replaced by their count
        /// <see cref="DailyRewardsState.ClaimedCount"/> — days are claimed strictly in order.
        /// </summary>
        private static void Migrate(JObject obj, int version, DailyRewardsState state)
        {
            if (version < 2)
                state.ClaimedCount = (obj["ClaimedDayIndices"] as JArray)?.Count ?? 0;

            state.Version = DailyRewardsState.CurrentVersion;
        }
    }
}
