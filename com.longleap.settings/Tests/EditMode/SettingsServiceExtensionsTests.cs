using NUnit.Framework;

namespace LongLeap.Settings.Tests
{
    public class SettingsServiceExtensionsTests
    {
        [Test]
        public void FindSetting_KnownKey_ReturnsSetting()
        {
            var service = new SettingsService(null);
            var group = new TestGroup();
            service.Register(group);

            Assert.AreSame(group.Count, service.FindSetting("test.count"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("missing.key")]
        public void FindSetting_UnknownOrEmptyKey_ReturnsNull(string key)
        {
            var service = new SettingsService(null);
            service.Register(new TestGroup());

            Assert.IsNull(service.FindSetting(key));
        }

        [Test]
        public void NullService_IsSafe()
        {
            ISettingsService service = null;

            Assert.IsNull(service.FindSetting("test.count"));
            Assert.DoesNotThrow(() => service.ResetToDefaults());
        }

        [Test]
        public void ResetToDefaults_RestoresEveryGroupAndFiresOnChanged()
        {
            var service = new SettingsService(null);
            var a = new TestGroup("settings.a");
            var b = new TestGroup("settings.b");
            service.Register(a);
            service.Register(b);
            service.SetValue(a.Volume, 0.9f);
            service.SetValue(a.Flag, false);
            service.SetValue(b.Quality, TestQuality.High);

            int calls = 0;
            service.OnChanged += _ => calls++;

            service.ResetToDefaults();

            Assert.AreEqual(0.5f, a.Volume.Value);
            Assert.IsTrue(a.Flag.Value);
            Assert.AreEqual(TestQuality.Medium, b.Quality.Value);
            Assert.AreEqual(3, calls);
        }
    }
}
