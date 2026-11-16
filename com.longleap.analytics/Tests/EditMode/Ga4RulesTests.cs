using NUnit.Framework;

namespace LongLeap.Analytics.Tests
{
    public class Ga4RulesTests
    {
        [Test]
        public void Truncate_DoesNotSplitSurrogatePair()
        {
            // "ab" + U+1F600 (two UTF-16 units); cutting at 3 would split the emoji.
            var value = "ab😀";

            Assert.AreEqual("ab", Ga4Rules.Truncate(value, 3));
            Assert.AreEqual(value, Ga4Rules.Truncate(value, 4));
        }

        [Test]
        public void Truncate_ShortOrNull_Unchanged()
        {
            Assert.AreEqual("abc", Ga4Rules.Truncate("abc", 10));
            Assert.IsNull(Ga4Rules.Truncate(null, 10));
        }

        [Test]
        public void NormalizeValue_SmallIntegerTypes_BecomeLong()
        {
            foreach (var value in new object[] { (sbyte)-1, (byte)1, (short)2, (ushort)3, 4u, 5UL })
            {
                Assert.IsTrue(Ga4Rules.TryNormalizeParamValue(value, out var normalized, out _), value.GetType().Name);
                Assert.IsInstanceOf<long>(normalized);
            }
        }

        [Test]
        public void NormalizeValue_HugeUlong_Rejected()
        {
            Assert.IsFalse(Ga4Rules.TryNormalizeParamValue(ulong.MaxValue, out _, out var reason));
            Assert.IsNotNull(reason);
        }

        [TestCase("app_install")]
        [TestCase("notification_send")]
        public void IsValidEventName_NewlyReserved_False(string name)
        {
            Assert.IsFalse(Ga4Rules.IsValidEventName(name, out _));
        }
    }
}
