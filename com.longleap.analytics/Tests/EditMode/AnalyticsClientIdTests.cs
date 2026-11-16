using NUnit.Framework;

namespace LongLeap.Analytics.Tests
{
    public class AnalyticsClientIdTests
    {
        [Test]
        public void GetOrCreate_FirstCall_CreatesAndPersists()
        {
            var prefs = new FakePlayerPrefs();

            var id = AnalyticsClientId.GetOrCreate(prefs);

            Assert.IsNotEmpty(id);
            Assert.AreEqual(id, prefs.Strings[AnalyticsClientId.DefaultKey]);
            Assert.AreEqual(1, prefs.SaveCount);
        }

        [Test]
        public void GetOrCreate_SecondCall_ReturnsSameId()
        {
            var prefs = new FakePlayerPrefs();

            var first = AnalyticsClientId.GetOrCreate(prefs);
            var second = AnalyticsClientId.GetOrCreate(prefs);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void Reset_NextCallCreatesNewId()
        {
            var prefs = new FakePlayerPrefs();
            var first = AnalyticsClientId.GetOrCreate(prefs);

            AnalyticsClientId.Reset(prefs);
            var second = AnalyticsClientId.GetOrCreate(prefs);

            Assert.AreNotEqual(first, second);
        }
    }
}
