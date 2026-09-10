using NUnit.Framework;
using LongLeap.Core;
using LongLeap.RemoteConfig.Storage;

namespace LongLeap.RemoteConfig.Tests
{
    [TestFixture]
    public class InMemoryPlayerPrefsTests
    {
        private InMemoryPlayerPrefs _prefs;

        [SetUp]
        public void SetUp()
        {
            _prefs = new InMemoryPlayerPrefs();
        }

        [Test]
        public void InMemoryPlayerPrefs_StoresAndRetrievesValues()
        {
            _prefs.SetString("test_str", "hello");
            _prefs.SetInt("test_int", 42);
            _prefs.SetFloat("test_float", 3.14f);

            Assert.AreEqual("hello", _prefs.GetString("test_str"));
            Assert.AreEqual(42, _prefs.GetInt("test_int"));
            Assert.AreEqual(3.14f, _prefs.GetFloat("test_float"));
        }

        [Test]
        public void InMemoryPlayerPrefs_HasKey_ReturnsCorrectly()
        {
            Assert.IsFalse(_prefs.HasKey("some_key"));
            _prefs.SetString("some_key", "value");
            Assert.IsTrue(_prefs.HasKey("some_key"));
            _prefs.DeleteKey("some_key");
            Assert.IsFalse(_prefs.HasKey("some_key"));
        }

        [Test]
        public void InMemoryPlayerPrefs_GetMissingKey_ReturnsDefault()
        {
            Assert.AreEqual("default_val", _prefs.GetString("missing", "default_val"));
            Assert.AreEqual(99, _prefs.GetInt("missing", 99));
            Assert.AreEqual(1.5f, _prefs.GetFloat("missing", 1.5f));
        }

        [Test]
        public void InMemoryPlayerPrefs_Save_IsNoOp()
        {
            Assert.DoesNotThrow(() => _prefs.Save());
        }
    }

    [TestFixture]
    public class AbExperimentInfoTests
    {
        [Test]
        public void AbExperimentInfo_Properties_ReturnConstructorValues()
        {
            var info = new AbExperimentInfo("exp_01", 2);

            Assert.AreEqual("exp_01", info.ExperimentId);
            Assert.AreEqual(2, info.VariantIndex);
        }
    }
}
