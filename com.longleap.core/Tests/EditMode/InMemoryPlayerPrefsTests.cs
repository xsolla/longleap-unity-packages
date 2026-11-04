using LongLeap.Core;
using NUnit.Framework;

namespace LongLeap.Core.Tests
{
    [TestFixture]
    public sealed class InMemoryPlayerPrefsTests
    {
        private InMemoryPlayerPrefs _prefs;

        [SetUp]
        public void SetUp()
        {
            _prefs = new InMemoryPlayerPrefs();
        }

        [Test]
        public void SetThenGet_ReturnsStoredValues()
        {
            _prefs.SetString("test_str", "hello");
            _prefs.SetInt("test_int", 42);
            _prefs.SetFloat("test_float", 3.14f);

            Assert.AreEqual("hello", _prefs.GetString("test_str"));
            Assert.AreEqual(42, _prefs.GetInt("test_int"));
            Assert.AreEqual(3.14f, _prefs.GetFloat("test_float"));
        }

        [Test]
        public void HasKey_FollowsSetAndDelete()
        {
            Assert.IsFalse(_prefs.HasKey("some_key"));
            _prefs.SetString("some_key", "value");
            Assert.IsTrue(_prefs.HasKey("some_key"));
            _prefs.DeleteKey("some_key");
            Assert.IsFalse(_prefs.HasKey("some_key"));
        }

        [Test]
        public void Get_MissingKey_ReturnsDefault()
        {
            Assert.AreEqual("default_val", _prefs.GetString("missing", "default_val"));
            Assert.AreEqual(99, _prefs.GetInt("missing", 99));
            Assert.AreEqual(1.5f, _prefs.GetFloat("missing", 1.5f));
        }
    }
}
