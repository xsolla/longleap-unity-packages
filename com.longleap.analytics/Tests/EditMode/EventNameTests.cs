using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Analytics.Tests
{
    public class EventNameTests
    {
        private static readonly Regex InvalidNameError = new Regex("^EventName: ");

        [TestCase("hero_selected")]
        [TestCase("a")]
        [TestCase("Level2_Complete")]
        public void Constructor_ValidName_IsValid(string value)
        {
            var name = new EventName(value);

            Assert.IsTrue(name.IsValid);
            Assert.AreEqual(value, name.Value);
        }

        [Test]
        public void Constructor_MaxLength_IsValid()
        {
            var name = new EventName(new string('a', 40));

            Assert.IsTrue(name.IsValid);
            Assert.AreEqual(40, name.Value.Length);
        }

        [TestCase("")]
        [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 41
        [TestCase("событие")]
        [TestCase("hero selected")]
        [TestCase("1hero")]
        [TestCase("_hero")]
        [TestCase("hero-selected")]
        [TestCase("firebase_custom")]
        [TestCase("google_event")]
        [TestCase("ga_event")]
        [TestCase("session_start")]
        [TestCase("first_visit")]
        public void Constructor_InvalidName_IsNoneAndLogsError(string value)
        {
            LogAssert.Expect(LogType.Error, InvalidNameError);

            var name = new EventName(value);

            Assert.IsFalse(name.IsValid);
            Assert.AreEqual(EventName.None, name);
            Assert.AreEqual(string.Empty, name.Value);
        }

        [Test]
        public void Constructor_Null_IsNoneAndLogsError()
        {
            LogAssert.Expect(LogType.Error, InvalidNameError);

            var name = new EventName(null);

            Assert.IsFalse(name.IsValid);
        }

        [Test]
        public void TryCreate_Valid_ReturnsTrue()
        {
            Assert.IsTrue(EventName.TryCreate("level_up", out var name));
            Assert.AreEqual("level_up", name.Value);
        }

        [Test]
        public void TryCreate_Invalid_ReturnsFalseWithoutLogging()
        {
            Assert.IsFalse(EventName.TryCreate("bad name", out var name));
            Assert.AreEqual(EventName.None, name);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Default_IsNone()
        {
            EventName name = default;

            Assert.IsFalse(name.IsValid);
            Assert.AreEqual(EventName.None, name);
            Assert.AreEqual("<none>", name.ToString());
        }

        [Test]
        public void Equality_SameValue_Equal()
        {
            var a = new EventName("test_event");
            var b = new EventName("test_event");

            Assert.IsTrue(a == b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_IsCaseSensitive()
        {
            Assert.IsTrue(new EventName("test_event") != new EventName("Test_Event"));
        }

        [Test]
        public void ToString_ReturnsValue()
        {
            Assert.AreEqual("test_event", new EventName("test_event").ToString());
        }
    }
}
