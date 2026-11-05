using LongLeap.Settings.UI;
using LongLeap.Tools;
using NUnit.Framework;
using UnityEngine;

namespace LongLeap.Settings.Tests
{
    public class SettingsWindowConfigSetTests
    {
        private SettingsWindowConfig _desktop;
        private SettingsWindowConfig _mobile;
        private SettingsWindowConfig _webDesktop;

        [SetUp]
        public void SetUp()
        {
            _desktop = ScriptableObject.CreateInstance<SettingsWindowConfig>();
            _mobile = ScriptableObject.CreateInstance<SettingsWindowConfig>();
            _webDesktop = ScriptableObject.CreateInstance<SettingsWindowConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_desktop);
            Object.DestroyImmediate(_mobile);
            Object.DestroyImmediate(_webDesktop);
        }

        [Test]
        public void Empty_ReturnsNullAndRequestedProfile()
        {
            var set = new SettingsWindowConfigSet();

            Assert.IsNull(set.Resolve(DeviceProfile.Mobile, out var used));
            Assert.AreEqual(DeviceProfile.Mobile, used);
        }

        [TestCase(DeviceProfile.Desktop)]
        [TestCase(DeviceProfile.Mobile)]
        [TestCase(DeviceProfile.WebDesktop)]
        [TestCase(DeviceProfile.WebMobile)]
        public void SingleConfig_IsUsedForEveryProfile(DeviceProfile profile)
        {
            var set = new SettingsWindowConfigSet { Desktop = _desktop };

            Assert.AreSame(_desktop, set.Resolve(profile, out var used));
            Assert.AreEqual(DeviceProfile.Desktop, used);
        }

        [TestCase(DeviceProfile.Desktop, DeviceProfile.Desktop)]
        [TestCase(DeviceProfile.WebDesktop, DeviceProfile.Desktop)]
        [TestCase(DeviceProfile.Mobile, DeviceProfile.Mobile)]
        [TestCase(DeviceProfile.WebMobile, DeviceProfile.Mobile)]
        public void WebProfile_FallsBackToNativeCounterpart(DeviceProfile profile, DeviceProfile expected)
        {
            var set = new SettingsWindowConfigSet { Desktop = _desktop, Mobile = _mobile };

            set.Resolve(profile, out var used);

            Assert.AreEqual(expected, used);
        }

        [Test]
        public void ExactProfile_WinsOverFallback()
        {
            var set = new SettingsWindowConfigSet { Desktop = _desktop, WebDesktop = _webDesktop };

            Assert.AreSame(_webDesktop, set.Resolve(DeviceProfile.WebDesktop, out var used));
            Assert.AreEqual(DeviceProfile.WebDesktop, used);
        }
    }
}
