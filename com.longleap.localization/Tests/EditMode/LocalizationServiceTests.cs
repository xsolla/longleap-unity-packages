using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Localization.Tests
{
    // Covers the paths that need no loaded string tables. Table loading and locale
    // switching depend on LocalizationSettings and Addressables and are not tested here.
    public class LocalizationServiceTests
    {
        private LocalizationService _service;

        [SetUp]
        public void SetUp() => _service = new LocalizationService(null);

        [TearDown]
        public void TearDown() => _service.Dispose();

        [Test]
        public void NewService_IsReady()
        {
            Assert.IsTrue(_service.IsReady);
        }

        [Test]
        public void Resolve_NullOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, _service.Resolve(null));
            Assert.AreEqual(string.Empty, _service.Resolve(string.Empty));
        }

        [Test]
        public void Resolve_MissingKey_ReturnsKey_AndWarnsOnce()
        {
            LogAssert.Expect(LogType.Warning, "[Localization] Missing translation for key 'menu_play'.");

            Assert.AreEqual("menu_play", _service.Resolve("menu_play"));
            Assert.AreEqual("menu_play", _service.Resolve("menu_play"));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ResolveFormat_MalformedFormat_ReturnsUnformatted()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Missing translation"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Failed to format 'bad\\{key'"));

            Assert.AreEqual("bad{key", _service.ResolveFormat("bad{key", 1));
        }

        [Test]
        public void SwitchLanguage_NullOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(_service.SwitchLanguage(null));
            Assert.IsFalse(_service.SwitchLanguage(string.Empty));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            _service.Dispose();

            Assert.DoesNotThrow(() => _service.Dispose());
        }
    }
}
