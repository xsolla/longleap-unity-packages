using System;
using LongLeap.Core;
using NUnit.Framework;

namespace LongLeap.Core.Tests
{
    [TestFixture]
    public class ResetScheduleTests
    {
        private static DateTime Utc(int day, int hour, int minute = 0) =>
            new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

        [Test]
        public void PeriodStart_AfterResetHour_IsToday()
        {
            Assert.AreEqual(Utc(8, 6), new ResetSchedule(6).PeriodStart(Utc(8, 12)));
        }

        [Test]
        public void PeriodStart_BeforeResetHour_IsYesterday()
        {
            Assert.AreEqual(Utc(7, 6), new ResetSchedule(6).PeriodStart(Utc(8, 5, 59)));
        }

        [Test]
        public void PeriodStart_ExactlyAtResetHour_IsNewPeriod()
        {
            Assert.AreEqual(Utc(8, 6), new ResetSchedule(6).PeriodStart(Utc(8, 6)));
        }

        [TestCase(25, 1)]
        [TestCase(-1, 23)]
        [TestCase(24, 0)]
        public void ResetHour_OutOfRange_IsWrapped(int hour, int expected)
        {
            Assert.AreEqual(expected, new ResetSchedule(hour).ResetHourUtc);
        }

        [Test]
        public void TimeUntilReset_PointsToNextResetHour()
        {
            Assert.AreEqual(TimeSpan.FromHours(1), new ResetSchedule(6).TimeUntilReset(Utc(8, 5)));
            Assert.AreEqual(TimeSpan.FromHours(18), new ResetSchedule(6).TimeUntilReset(Utc(8, 12)));
        }
    }
}
