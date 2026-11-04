using NUnit.Framework;
using UnityEngine;

namespace LongLeap.Core.Tests
{
    // Writes real editor PlayerPrefs under its own keys and removes them afterwards.
    [TestFixture]
    public sealed class UnityPlayerPrefsAdapterTests
    {
        private const string StringKey = "longleap.core.tests.prefs.string";
        private const string IntKey = "longleap.core.tests.prefs.int";
        private const string FloatKey = "longleap.core.tests.prefs.float";

        private UnityPlayerPrefsAdapter _adapter;

        [SetUp]
        public void SetUp()
        {
            DeleteKeys();
            _adapter = new UnityPlayerPrefsAdapter();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteKeys();
        }

        private static void DeleteKeys()
        {
            PlayerPrefs.DeleteKey(StringKey);
            PlayerPrefs.DeleteKey(IntKey);
            PlayerPrefs.DeleteKey(FloatKey);
        }

        [Test]
        public void SetGetHasDelete_RoundTripsEveryType()
        {
            _adapter.SetString(StringKey, "hello");
            _adapter.SetInt(IntKey, 42);
            _adapter.SetFloat(FloatKey, 3.14f);

            Assert.That(_adapter.GetString(StringKey), Is.EqualTo("hello"));
            Assert.That(_adapter.GetInt(IntKey), Is.EqualTo(42));
            Assert.That(_adapter.GetFloat(FloatKey), Is.EqualTo(3.14f));
            Assert.That(_adapter.HasKey(StringKey), Is.True);

            _adapter.DeleteKey(StringKey);

            Assert.That(_adapter.HasKey(StringKey), Is.False);
        }

        [Test]
        public void MissingKey_ReturnsDefaults()
        {
            Assert.That(_adapter.HasKey(StringKey), Is.False);
            Assert.That(_adapter.GetString(StringKey, "fallback"), Is.EqualTo("fallback"));
            Assert.That(_adapter.GetInt(IntKey, 99), Is.EqualTo(99));
            Assert.That(_adapter.GetFloat(FloatKey, 1.23f), Is.EqualTo(1.23f));
        }
    }
}
