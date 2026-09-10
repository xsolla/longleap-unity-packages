namespace LongLeap.DailyRewards
{
    public sealed class CalendarDay
    {
        public DailyRewardDay Day { get; init; }
        public CalendarDayState State { get; init; }
    }
}