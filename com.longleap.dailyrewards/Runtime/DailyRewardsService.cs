using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.DailyRewards
{
    public sealed class DailyRewardsService : IDailyRewardsService
    {
        private readonly IDailyRewardsConfig _config;
        private readonly IDailyRewardsStateStore _store;
        private readonly IDailyRewardsGameBridge _bridge;
        private DailyRewardsState _state;
        private bool _disabled;

        public DailyRewardsService(
            IDailyRewardsConfig config,
            IDailyRewardsStateStore store,
            IDailyRewardsGameBridge bridge)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public async Task InitializeAsync()
        {
            _state = await _store.LoadAsync();
            if (_config.DayCount <= 0)
            {
                _disabled = true;
                Debug.LogWarning("DailyRewardsService: DayCount = 0, отключаю ежедневные награды.");
            }
        }

        public bool CanClaimToday
        {
            get
            {
                if (_disabled || _state == null) return false;
                if (IsCycleComplete) return false;
                return IsNewResetWindow();
            }
        }

        public bool IsCycleComplete
        {
            get
            {
                if (_disabled) return true;
                if (_config.CyclePolicy == DailyRewardCyclePolicy.Once)
                {
                    var dayCount = _config.DayCount;
                    if (dayCount <= 0) return true;
                    var claimedCount = _state?.ClaimedDayIndices?.Length ?? 0;
                    return claimedCount >= dayCount;
                }
                return false;
            }
        }

        public int Streak => _state?.Streak ?? 0;

        public int MaxConsecutiveStreak => _state?.MaxConsecutiveStreak ?? 0;

        public bool Claim()
        {
            if (_disabled || _state == null) return false;
            if (!CanClaimToday) return false;

            var now = DateTime.UtcNow;

            if (_state.Streak > 0)
            {
                var lastClaim = new DateTime(_state.LastClaimUtcTicks, DateTimeKind.Utc);
                if (lastClaim < LastResetUtc(now).Subtract(_config.GracePeriod))
                {
                    _state.Streak = 0;
                    _bridge.OnStreakReset();
                }
            }

            var nextIndex = GetNextDayIndex();
            if (nextIndex < 0)
                return false;

            var indices = _state.ClaimedDayIndices?.ToList() ?? new System.Collections.Generic.List<int>();
            indices.Add(nextIndex);
            _state.ClaimedDayIndices = indices.ToArray();

            _state.Streak++;
            if (_state.Streak > _state.MaxConsecutiveStreak)
                _state.MaxConsecutiveStreak = _state.Streak;

            _state.LastClaimUtcTicks = now.Ticks;

            try { _bridge.OnStreakChanged(_state.Streak, _state.MaxConsecutiveStreak); }
            catch (Exception e) { Debug.LogError($"DailyRewardsService: ошибка в OnStreakChanged: {e.Message}"); }

            var dayIndex = nextIndex;

            if (indices.Count >= _config.DayCount)
            {
                _state.CurrentCycleNumber++;
                try { _bridge.OnCycleCompleted(_state.CurrentCycleNumber); }
                catch (Exception e) { Debug.LogError($"DailyRewardsService: ошибка в OnCycleCompleted: {e.Message}"); }

                if (_config.CyclePolicy == DailyRewardCyclePolicy.Loop)
                {
                    _state.ClaimedDayIndices = Array.Empty<int>();
                }
            }

            var result = new DailyRewardClaimResult
            {
                Day = GetDayForIndex(dayIndex),
                Streak = _state.Streak,
                MaxStreak = _state.MaxConsecutiveStreak,
                IsCycleComplete = IsCycleComplete
            };

            try
            {
                _bridge.ApplyReward(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"DailyRewardsService: Ошибка при ApplyReward: {ex.Message}");
            }

            SaveStateAsync();

            return true;
        }

        public CalendarDay[] GetCalendar()
        {
            if (_disabled || _state == null)
                return Array.Empty<CalendarDay>();

            var claimed = _state.ClaimedDayIndices?.ToHashSet() ?? new System.Collections.Generic.HashSet<int>();
            var nextIndex = GetNextDayIndex();
            var canClaim = CanClaimToday;
            var days = new CalendarDay[_config.DayCount];

            for (int i = 0; i < _config.DayCount; i++)
            {
                var day = GetDayForIndex(i);
                CalendarDayState state;

                if (claimed.Contains(i))
                {
                    state = CalendarDayState.Claimed;
                }
                else if (i == nextIndex)
                {
                    state = canClaim ? CalendarDayState.Claimable : CalendarDayState.Locked;
                }
                else
                {
                    state = CalendarDayState.Future;
                }

                days[i] = new CalendarDay
                {
                    Day = day,
                    State = state
                };
            }

            return days;
        }

        public void Reset()
        {
            if (_state == null) return;

            _state.ClaimedDayIndices = Array.Empty<int>();
            _state.Streak = 0;
            _state.MaxConsecutiveStreak = 0;
            _state.LastClaimUtcTicks = 0;
            _state.CurrentCycleNumber = 0;

            SaveStateAsync();
        }

        public void DebugSkipToNextResetWindow()
        {
            if (_disabled || _state == null) return;

            var lastReset = LastResetUtc(DateTime.UtcNow);
            _state.LastClaimUtcTicks = lastReset.AddTicks(-1).Ticks;
        }

        private int GetNextDayIndex()
        {
            var claimed = _state.ClaimedDayIndices;
            var count = _config.DayCount;
            if (count <= 0) return -1;

            var claimedSet = claimed?.ToHashSet() ?? new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                if (!claimedSet.Contains(i))
                    return i;
            }

            return -1;
        }

        private bool IsNewResetWindow()
        {
            var lastReset = LastResetUtc(DateTime.UtcNow);
            return _state.LastClaimUtcTicks < lastReset.Ticks;
        }

        private static DateTime LastResetUtc(DateTime now, int resetHourUtc)
        {
            var reset = now.Date.AddHours(resetHourUtc % 24);
            if (now < reset)
                reset = reset.AddDays(-1);
            return reset;
        }

        private DateTime LastResetUtc(DateTime now)
        {
            return LastResetUtc(now, _config.ResetHourUtc);
        }

        private DailyRewardDay GetDayForIndex(int index)
        {
            var days = _config.Days;
            if (days != null && index >= 0 && index < days.Length && days[index] != null)
                return days[index];

            return new DailyRewardDay
            {
                DayIndex = index,
                Rewards = Array.Empty<RewardEntry>()
            };
        }

        private async void SaveStateAsync()
        {
            try
            {
                await _store.SaveAsync(_state);
            }
            catch (Exception ex)
            {
                Debug.LogError($"DailyRewardsService: Ошибка сохранения состояния: {ex.Message}");
            }
        }
    }
}