using NUnit.Framework;

namespace LongLeap.Tools.Tests
{
    /// <summary>Profile override precedence and the two axes derived from a profile.</summary>
    public sealed class DeviceInfoTests
    {
        [TearDown]
        public void TearDown() => DeviceInfo.Override = null;

        [TestCase(DeviceProfile.Desktop, false, false)]
        [TestCase(DeviceProfile.Mobile, false, true)]
        [TestCase(DeviceProfile.WebDesktop, true, false)]
        [TestCase(DeviceProfile.WebMobile, true, true)]
        public void Override_DrivesBothAxes(DeviceProfile profile, bool web, bool mobile)
        {
            DeviceInfo.Override = profile;

            Assert.AreEqual(profile, DeviceInfo.Profile);
            Assert.AreEqual(web, DeviceInfo.IsWeb);
            Assert.AreEqual(mobile, DeviceInfo.IsMobile);
            Assert.AreEqual(!mobile, DeviceInfo.IsDesktop);
        }

        [Test]
        public void Detect_InEditor_IsNotWeb()
        {
            // The editor is not WebGLPlayer, even with WebGL as the build target.
            var detected = DeviceInfo.Detect();

            Assert.IsFalse(detected is DeviceProfile.WebDesktop or DeviceProfile.WebMobile);
        }
    }
}
