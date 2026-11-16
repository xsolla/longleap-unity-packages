using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;
using UnityEngine;

namespace LongLeap.DailyRewards
{
    /// <summary>
    /// Daily rewards calendar. The rules (what can be claimed, how the streak and the
    /// cycle change) live in <see cref="DailyRewardsRules"/>; this class loads and saves state,
    /// owns the clock and events, and grants rewards through the bridge.
    /// </summary>
    public sealed class DailyRewardsService : IDailyRewardsService, IDailyRewardsDebug
    {
        private readonly IDailyRewardsConfig _config;
        private readonly IDailyRewardsStateStore _store;
        private readonly IDailyRewardsGameBridge _bridge;
        private readonly Func<DateTime> _utcNow;
        private DailyRewardsState _state;
        private DailyRewardDay[] _days = Array.Empty<DailyRewardDay>();
        private DailyRewardsRules _rules;
        private bool _disabled;
        private bool _claiming;
        private long _windowStartTicks;
        private Task<bool> _saving = Task.FromResult(true);

        public event Action Changed;

        /// <param name="utcNow">Clock. Defaults to <see cref="DateTime.UtcNow"/>; replaced in tests,
        /// and the game can pass server time here.</param>
        public DailyRewardsService(
            IDailyRewardsConfig config,
            IDailyRewardsStateStore store,
            IDailyRewardsGameBridge bridge,
            Func<DateTime> utcNow = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public async Task InitializeAsync()
        {
            _state = null;

            // Validate the config and snapshot it once: the service works with the copy,
            // so editing the asset mid-game cannot reshape the calendar under the saved state.
            LogValidation(DailyRewardsConfigValidator.Validate(_config));
            _days = SnapshotDays(_config.Days);
            _rules = new DailyRewardsRules(_days.Length, _config.CyclePolicy, _config.GracePeriod,
                new ResetSchedule(_config.ResetHourUtc));

            var loaded = await _store.LoadAsync();
            var now = _utcNow();
            DailyRewardsState state;
            var dirty = false;

            switch (loaded.Status)
            {
                case DailyRewardsStateLoadStatus.Loaded:
                    state = loaded.State;
                    break;

                case DailyRewardsStateLoadStatus.Empty:
                    state = DailyRewardsState.Empty();
                    break;

                case DailyRewardsStateLoadStatus.Unreadable:
                    // A save exists but cannot be read: whether today's day was claimed
                    // is unknown. Start the calendar over, but treat today's window
                    // as used — otherwise the reward could be claimed twice.
                    // Save right away so the next launch does not end up here again.
                    Debug.LogWarning("DailyRewardsService: save is unreadable — the calendar starts over from the next reset.");
                    state = DailyRewardsState.Empty();
                    state.LastClaimUtcTicks = now.Ticks;
                    dirty = true;
                    break;

                default:
                    // The store did not answer. Starting from scratch is not an option: an already
                    // claimed day would be granted again.
                    Debug.LogError("DailyRewardsService: state failed to load — daily rewards are disabled until restart.");
                    return;
            }

            _state = state;
            _windowStartTicks = _rules.Schedule.PeriodStart(now).Ticks;

            if (_days.Length == 0)
            {
                _disabled = true;
                Debug.LogWarning("DailyRewardsService: the calendar has no days, disabling daily rewards.");
            }

            if (dirty)
                await Save(_state);
        }

        public bool IsReady => _state != null && !_disabled;

        public bool CanClaimToday => IsReady && !_claiming && _rules.CanClaim(_state, _utcNow());

        public bool IsCycleComplete
        {
            get
            {
                if (_disabled) return true;
                return _state != null && _rules.IsCycleComplete(_state);
            }
        }

        public int Streak => _state?.Streak ?? 0;

        public int MaxConsecutiveStreak => _state?.MaxConsecutiveStreak ?? 0;

        public TimeSpan TimeUntilNextClaim
        {
            get
            {
                if (!IsReady || CanClaimToday || IsCycleComplete) return TimeSpan.Zero;
                return _rules.Schedule.TimeUntilReset(_utcNow());
            }
        }

        public void Tick()
        {
            if (!IsReady) return;

            var windowStart = _rules.Schedule.PeriodStart(_utcNow()).Ticks;
            if (windowStart == _windowStartTicks) return;

            _windowStartTicks = windowStart;
            RaiseChanged();
        }

        public async Task<bool> ClaimAsync()
        {
            if (!CanClaimToday) return false;

            var previous = _state;
            if (!_rules.TryClaim(previous, _utcNow(), out var next, out var outcome))
                return false;

            // Save the claimed day first, then grant the reward: if the save fails there is
            // no reward, otherwise the same day could be claimed again after a restart.
            // If the game crashes between the save and the grant, the player loses one reward
            // but never gets it twice. While the save is in flight CanClaimToday is false,
            // so a second press gets false.
            bool saved;
            _claiming = true;
            try { saved = await Save(next); }
            finally { _claiming = false; }

            if (!saved)
                return false;

            // A debug command may have reset the state during the save — do not overwrite it.
            if (ReferenceEquals(_state, previous))
                _state = next;

            var result = new DailyRewardClaimResult
            {
                Day = _days[outcome.DayIndex],
                Streak = next.Streak,
                MaxStreak = next.MaxConsecutiveStreak,
                StreakWasReset = outcome.StreakWasReset,
                CompletedCycle = outcome.CompletedCycle,
                CycleNumber = next.CurrentCycleNumber,
                IsCycleComplete = _rules.IsCycleComplete(next)
            };

            // A game-side error does not roll the claim back: the day is already saved.
            try
            {
                _bridge.ApplyReward(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"DailyRewardsService: ApplyReward failed: {ex}");
            }

            RaiseChanged();
            return true;
        }

        public CalendarDay[] GetCalendar()
        {
            if (!IsReady)
                return Array.Empty<CalendarDay>();

            var claimed = _rules.ClaimedInCycle(_state, _utcNow());
            var canClaim = CanClaimToday;
            var days = new CalendarDay[_days.Length];

            for (int i = 0; i < days.Length; i++)
            {
                days[i] = new CalendarDay
                {
                    Day = _days[i],
                    State = DailyRewardsRules.DayState(i, claimed, canClaim)
                };
            }

            return days;
        }

        void IDailyRewardsDebug.ResetState()
        {
            if (_state == null) return;

            _state = DailyRewardsState.Empty();
            _ = Save(_state);
            RaiseChanged();
        }

        void IDailyRewardsDebug.SkipToNextResetWindow()
        {
            if (!IsReady) return;

            var next = _state.Clone();
            next.LastClaimUtcTicks = _rules.Schedule.PeriodStart(_utcNow()).Ticks - 1;
            _state = next;
            RaiseChanged();
        }

        // Copy of the days indexed by position; a missing day becomes a day without rewards.
        private static DailyRewardDay[] SnapshotDays(IReadOnlyList<DailyRewardDay> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<DailyRewardDay>();

            var days = new DailyRewardDay[source.Count];
            for (int i = 0; i < days.Length; i++)
            {
                var day = source[i];
                days[i] = day != null && day.DayIndex == i && day.Rewards != null
                    ? day
                    : new DailyRewardDay
                    {
                        DayIndex = i,
                        Rewards = day?.Rewards ?? Array.Empty<RewardEntry>()
                    };
            }

            return days;
        }

        private static void LogValidation(DailyRewardsConfigValidationResult result)
        {
            foreach (var error in result.Errors)
                Debug.LogError($"DailyRewardsService: {error}");
            foreach (var warning in result.Warnings)
                Debug.LogWarning($"DailyRewardsService: {warning}");
        }

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Debug.LogError($"DailyRewardsService: a Changed subscriber failed: {ex}"); }
        }

        // Saves run strictly one after another — each waits for the previous one, otherwise an
        // older state could land on top of a newer one. false — the save failed (already logged).
        private Task<bool> Save(DailyRewardsState state) => _saving = SaveAfter(_saving, state);

        private async Task<bool> SaveAfter(Task previous, DailyRewardsState state)
        {
            await previous;

            try
            {
                await _store.SaveAsync(state);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"DailyRewardsService: failed to save state: {ex}");
                return false;
            }
        }
    }
}
