using System.Threading.Tasks;

namespace LongLeap.DailyRewards
{
    public interface IDailyRewardsService
    {
        Task InitializeAsync();
        bool CanClaimToday { get; }
        bool IsCycleComplete { get; }
        int Streak { get; }
        int MaxConsecutiveStreak { get; }
        bool Claim();
        CalendarDay[] GetCalendar();
        void Reset();
        void DebugSkipToNextResetWindow();
    }
}