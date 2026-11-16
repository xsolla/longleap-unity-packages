using System;
using System.Collections.Generic;
using LongLeap.Core;
using UnityEngine;

namespace LongLeap.DailyRewards
{
    [Serializable]
    public sealed class RewardEntryDef
    {
        [Tooltip("Reward key the game understands (for example, a currency id).")]
        [SerializeField] private string _rewardType = "xp";
        [SerializeField] private string _rewardId;
        [SerializeField] private int _amount;

        // Empty strings from the inspector become null: RewardKey/RewardId throw
        // ArgumentException on "", and an unfilled inspector field is exactly "",
        // which would break the whole calendar.
        public RewardEntry ToRewardEntry() => new()
        {
            Key = new RewardKey(string.IsNullOrEmpty(_rewardType) ? null : _rewardType),
            RewardId = new RewardId(string.IsNullOrEmpty(_rewardId) ? null : _rewardId),
            Amount = _amount
        };
    }

    [Serializable]
    public sealed class DailyRewardDayDef
    {
        [SerializeField] private RewardEntryDef[] _rewards;

        public DailyRewardDay ToRewardDay(int dayIndex) => new()
        {
            DayIndex = dayIndex,
            Rewards = _rewards != null
                ? Array.ConvertAll(_rewards, r => r != null ? r.ToRewardEntry() : null)
                : Array.Empty<RewardEntry>()
        };
    }

    [CreateAssetMenu(fileName = "DefaultDailyRewardConfig", menuName = "LongLeap/Daily Rewards/Config")]
    public sealed class DefaultDailyRewardConfigSO : ScriptableObject, IDailyRewardsConfig
    {
        [Tooltip("Calendar days in order: one element per day. An empty list disables rewards.")]
        [SerializeField] private DailyRewardDayDef[] _days;
        [SerializeField] private DailyRewardCyclePolicy _cyclePolicy = DailyRewardCyclePolicy.Loop;
        [SerializeField][Range(0, 23)] private int _resetHourUtc;
        [Tooltip("How long before the latest reset the last claim may be for the streak to " +
            "survive. One day (default) — missing any day resets the streak; each further " +
            "24 h forgives one more missed day. Set in ticks, as a multiple of a day.")]
        [SerializeField] private long _gracePeriodTicks = TimeSpan.TicksPerDay;

        // Days is built from the serialized definitions once: the service snapshots it
        // on initialization and the inspector reads it on every validation. Reset in
        // OnValidate (inspector edit) and OnEnable (asset reload).
        private DailyRewardDay[] _cachedDays;

        /// <summary>Calendar days. The same list on every call.</summary>
        public IReadOnlyList<DailyRewardDay> Days => _cachedDays ??= BuildDays();

        public DailyRewardCyclePolicy CyclePolicy => _cyclePolicy;
        public int ResetHourUtc => _resetHourUtc;
        public TimeSpan GracePeriod => TimeSpan.FromTicks(_gracePeriodTicks);

        private DailyRewardDay[] BuildDays()
        {
            if (_days == null)
                return Array.Empty<DailyRewardDay>();

            var days = new DailyRewardDay[_days.Length];
            for (int i = 0; i < _days.Length; i++)
                days[i] = _days[i]?.ToRewardDay(i);
            return days;
        }

        private void OnEnable() => _cachedDays = null;

        private void OnValidate()
        {
            _cachedDays = null;

            var result = DailyRewardsConfigValidator.Validate(this);
            foreach (var error in result.Errors)
                Debug.LogError($"{name}: {error}", this);
            foreach (var warning in result.Warnings)
                Debug.LogWarning($"{name}: {warning}", this);
        }
    }
}
