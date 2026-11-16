using NUnit.Framework;

namespace LongLeap.Settings.Tests
{
    public class SettingTests
    {
        [Test]
        public void NewSetting_StartsAtDefault()
        {
            var setting = new Setting<bool>("flag", true, "flag_label");

            Assert.IsTrue(setting.Value);
            Assert.IsTrue(setting.DefaultValue);
            Assert.AreEqual(typeof(bool), setting.ValueType);
            Assert.AreEqual(true, setting.BoxedValue);
            Assert.AreEqual("flag", setting.Key);
            Assert.AreEqual("flag_label", setting.LabelKey);
        }

        [TestCase(-1f, 0f)]
        [TestCase(0.25f, 0.25f)]
        [TestCase(2f, 1f)]
        public void NumericFloat_IsClampedToRange(float input, float expected)
        {
            var service = new SettingsService(null);
            var setting = new NumericSetting<float>("f", 0.5f, 0f, 1f, "f");

            service.SetValue(setting, input);

            Assert.AreEqual(expected, setting.Value);
        }

        [TestCase(-5, 0)]
        [TestCase(42, 42)]
        [TestCase(500, 100)]
        public void NumericInt_IsClampedToRange(int input, int expected)
        {
            var service = new SettingsService(null);
            var setting = new NumericSetting<int>("i", 10, 0, 100, "i");

            service.SetValue(setting, input);

            Assert.AreEqual(expected, setting.Value);
        }

        [Test]
        public void EnumSetting_IndexRoundTrips()
        {
            var service = new SettingsService(null);
            var setting = new EnumSetting<TestQuality>("q", TestQuality.Medium, "q");
            IEnumSetting asEnum = setting;

            Assert.AreEqual(1, asEnum.GetOptionIndex());

            asEnum.ApplyIndex(2, service);

            Assert.AreEqual(TestQuality.High, setting.Value);
            Assert.AreEqual(2, asEnum.GetOptionIndex());
        }
    }
}
