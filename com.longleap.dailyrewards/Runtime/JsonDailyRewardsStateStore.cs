using System.Threading.Tasks;
using LongLeap.Core;
using Newtonsoft.Json;

namespace LongLeap.DailyRewards
{
    public sealed class JsonDailyRewardsStateStore : IDailyRewardsStateStore
    {
        private const int CurrentVersion = 1;
        private const string DefaultStorageKey = "daily_rewards_state";

        private readonly IPersistentStorage _storage;
        private readonly string _storageKey;

        public JsonDailyRewardsStateStore(IPersistentStorage storage, string storageKey = null)
        {
            _storage = storage;
            _storageKey = storageKey ?? DefaultStorageKey;
        }

        public async Task<DailyRewardsState> LoadAsync()
        {
            var json = await _storage.LoadAsync(_storageKey);
            return ParseState(json);
        }

        public async Task SaveAsync(DailyRewardsState state)
        {
            var json = JsonConvert.SerializeObject(state, Formatting.Indented);
            await _storage.SaveAsync(_storageKey, json);
        }

        private DailyRewardsState ParseState(string json)
        {
            if (string.IsNullOrEmpty(json))
                return EmptyState();

            DailyRewardsState state;
            try
            {
                state = JsonConvert.DeserializeObject<DailyRewardsState>(json);
            }
            catch
            {
                return EmptyState();
            }

            if (state == null)
                return EmptyState();

            if (state.Version > CurrentVersion)
                return EmptyState();

            return state;
        }

        private static DailyRewardsState EmptyState()
        {
            return new DailyRewardsState
            {
                Version = CurrentVersion,
                ClaimedDayIndices = System.Array.Empty<int>(),
                Streak = 0,
                MaxConsecutiveStreak = 0,
                LastClaimUtcTicks = 0,
                CurrentCycleNumber = 0
            };
        }
    }
}
