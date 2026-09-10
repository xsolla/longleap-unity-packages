using LongLeap.Platform.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LongLeap.Platform.Unity.Tests
{
    [TestFixture]
    public sealed class UnityPlayerPrefsAdapterTests
    {
        private UnityPlayerPrefsAdapter _adapter;

        private const string TestKey = "rc_adapter_test_key";

        [SetUp]
        public void SetUp()
        {
            _adapter = new UnityPlayerPrefsAdapter();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(TestKey);
        }

        [Test]
        public void SetString_GetString_RoundTrips()
        {
            _adapter.SetString(TestKey, "hello");

            Assert.That(_adapter.GetString(TestKey), Is.EqualTo("hello"));
        }

        [Test]
        public void SetInt_GetInt_RoundTrips()
        {
            _adapter.SetInt(TestKey, 42);

            Assert.That(_adapter.GetInt(TestKey), Is.EqualTo(42));
        }

        [Test]
        public void SetFloat_GetFloat_RoundTrips()
        {
            _adapter.SetFloat(TestKey, 3.14f);

            Assert.That(_adapter.GetFloat(TestKey), Is.EqualTo(3.14f));
        }

        [Test]
        public void HasKey_ReturnsCorrectly()
        {
            Assert.That(_adapter.HasKey(TestKey), Is.False);

            _adapter.SetString(TestKey, "value");

            Assert.That(_adapter.HasKey(TestKey), Is.True);

            _adapter.DeleteKey(TestKey);

            Assert.That(_adapter.HasKey(TestKey), Is.False);
        }

        [Test]
        public void GetMissingKey_ReturnsDefault()
        {
            Assert.That(_adapter.GetString(TestKey, "fallback"), Is.EqualTo("fallback"));
            Assert.That(_adapter.GetInt(TestKey, 99), Is.EqualTo(99));
            Assert.That(_adapter.GetFloat(TestKey, 1.23f), Is.EqualTo(1.23f));
        }
    }
}
