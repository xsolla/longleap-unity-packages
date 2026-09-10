using System;
using UnityEngine;

namespace LongLeap.DailyRewards
{
    [Serializable]
    public sealed class RewardEntryDef
    {
        [SerializeField] private string _rewardType = "xp";
        [SerializeField] private string _rewardId;
        [SerializeField] private int _amount;

        // Пустые строки из инспектора приводим к null: RewardKey/RewardId на
        // "" бросают ArgumentException, а незаполненное поле в инспекторе — это
        // именно "", и на нём разваливался бы весь календарь.
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
        [SerializeField] private int _dayIndex;
        [SerializeField] private RewardEntryDef[] _rewards;

        public DailyRewardDay ToRewardDay() => new()
        {
            DayIndex = _dayIndex,
            Rewards = _rewards != null
                ? Array.ConvertAll(_rewards, r => r != null ? r.ToRewardEntry() : null)
                : Array.Empty<RewardEntry>()
        };
    }

    [CreateAssetMenu(fileName = "DefaultDailyRewardConfig", menuName = "LongLeap/Daily Rewards/Config")]
    public sealed class DefaultDailyRewardConfigSO : ScriptableObject, IDailyRewardsConfig
    {
        [SerializeField] private int _dayCount = 7;
        [SerializeField] private DailyRewardDayDef[] _days;
        [SerializeField] private DailyRewardCyclePolicy _cyclePolicy = DailyRewardCyclePolicy.Loop;
        [SerializeField][Range(0, 23)] private int _resetHourUtc;
        [SerializeField] private long _gracePeriodTicks = TimeSpan.TicksPerDay;

        public int DayCount => _dayCount;

        public DailyRewardDay[] Days
        {
            get
            {
                if (_days == null)
                    return Array.Empty<DailyRewardDay>();
                return Array.ConvertAll(_days, d => d != null ? d.ToRewardDay() : null);
            }
        }

        public DailyRewardCyclePolicy CyclePolicy => _cyclePolicy;
        public int ResetHourUtc => _resetHourUtc;
        public TimeSpan GracePeriod => TimeSpan.FromTicks(_gracePeriodTicks);
    }
}