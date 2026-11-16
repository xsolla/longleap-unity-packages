using NUnit.Framework;

namespace LongLeap.Localization.Tests
{
    public class LocKeyTests
    {
        [Test]
        public void ImplicitConversions_RoundTripValue()
        {
            LocKey key = "test_key";
            string value = key;

            Assert.AreEqual("test_key", value);
            Assert.AreEqual("test_key", key.ToString());
        }

        [TestCase(null)]
        [TestCase("")]
        public void NullOrEmpty_IsEmptyAndEqualsDefault(string value)
        {
            var key = new LocKey(value);

            Assert.IsTrue(key.IsEmpty);
            Assert.IsTrue(default(LocKey).IsEmpty);
            Assert.AreEqual(default(LocKey), key);
            Assert.AreEqual(default(LocKey).GetHashCode(), key.GetHashCode());
            Assert.AreEqual(string.Empty, key.ToString());
        }

        [Test]
        public void NonEmpty_IsNotEmpty()
        {
            Assert.IsFalse(new LocKey("test").IsEmpty);
        }

        [Test]
        public void SameValue_AreEqual()
        {
            LocKey a = "key";
            LocKey b = new LocKey("key");

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void DifferentValue_AreNotEqual()
        {
            LocKey a = "key_a";
            LocKey b = "key_b";

            Assert.IsFalse(a.Equals(b));
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Format_BuildsKey()
        {
            Assert.AreEqual(new LocKey("enemy_goblin_name"), LocKey.Format("enemy_{0}_name", "goblin"));
        }
    }
}
