using NUnit.Framework;

namespace LongLeap.Analytics.Tests
{
    public class AnalyticsEventBaseTests
    {
        private sealed class TestEvent : AnalyticsEventBase
        {
            public TestEvent() : base(new EventName("test_event"))
            {
            }

            public void Set(string key, string value) => SetParam(key, value);
            public void Set(string key, long value) => SetParam(key, value);
            public void Set(string key, double value) => SetParam(key, value);
            public void Set(string key, bool value) => SetParam(key, value);
            public bool Remove(string key) => RemoveParam(key);
        }

        [Test]
        public void New_HasNameAndNoParams()
        {
            var e = new TestEvent();

            Assert.AreEqual("test_event", e.Name.Value);
            Assert.IsEmpty(e.Params);
        }

        [Test]
        public void SetParam_SupportedTypes_StoredNormalized()
        {
            var e = new TestEvent();
            e.Set("text", "ronin");
            e.Set("count", 3);          // int binds to long
            e.Set("distance", 1.5f);    // float binds to double
            e.Set("is_critical", true); // bool is sent as a string

            Assert.AreEqual("ronin", e.Params["text"]);
            Assert.AreEqual(3L, e.Params["count"]);
            Assert.AreEqual(1.5d, e.Params["distance"]);
            Assert.AreEqual("true", e.Params["is_critical"]);
        }

        [TestCase("")]
        [TestCase("ключ")]
        [TestCase("my key")]
        [TestCase("_private")]
        [TestCase("firebase_value")]
        [TestCase("session_id")]
        [TestCase("engagement_time_msec")]
        public void SetParam_InvalidOrReservedKey_Skipped(string key)
        {
            var e = new TestEvent();

            e.Set(key, "value");

            Assert.IsEmpty(e.Params);
        }

        [Test]
        public void SetParam_LongString_CutTo100()
        {
            var e = new TestEvent();

            e.Set("key", new string('x', 150));

            Assert.AreEqual(100, ((string)e.Params["key"]).Length);
        }

        [Test]
        public void SetParam_StringOfExactly100_Kept()
        {
            var e = new TestEvent();

            e.Set("key", new string('x', 100));

            Assert.AreEqual(100, ((string)e.Params["key"]).Length);
        }

        [Test]
        public void SetParam_NullString_RemovesParam()
        {
            var e = new TestEvent();
            e.Set("key", "value");

            e.Set("key", (string)null);

            Assert.IsFalse(e.Params.ContainsKey("key"));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void SetParam_NonFiniteDouble_Skipped(double value)
        {
            var e = new TestEvent();

            e.Set("key", value);

            Assert.IsEmpty(e.Params);
        }

        [Test]
        public void SetParam_SameKey_Overwrites()
        {
            var e = new TestEvent();

            e.Set("key", "first");
            e.Set("key", "second");

            Assert.AreEqual(1, e.Params.Count);
            Assert.AreEqual("second", e.Params["key"]);
        }

        [Test]
        public void SetParam_UpToMaxCustomParams_AllStored()
        {
            var e = new TestEvent();

            for (var i = 0; i < Ga4Rules.MaxCustomParams; i++)
                e.Set($"key{i}", i);

            Assert.AreEqual(Ga4Rules.MaxCustomParams, e.Params.Count);
        }

        [Test]
        public void SetParam_BeyondMaxCustomParams_Skipped()
        {
            var e = new TestEvent();
            for (var i = 0; i < Ga4Rules.MaxCustomParams; i++)
                e.Set($"key{i}", i);

            e.Set("key_extra", 1);
            e.Set("key0", 100); // overwriting an existing key still works when full

            Assert.AreEqual(Ga4Rules.MaxCustomParams, e.Params.Count);
            Assert.IsFalse(e.Params.ContainsKey("key_extra"));
            Assert.AreEqual(100L, e.Params["key0"]);
        }

        [Test]
        public void RemoveParam_RemovesExisting()
        {
            var e = new TestEvent();
            e.Set("key", 1);

            Assert.IsTrue(e.Remove("key"));
            Assert.IsFalse(e.Remove("key"));
            Assert.IsEmpty(e.Params);
        }

        [Test]
        public void AnalyticsEvent_With_ChainsParams()
        {
            var e = new AnalyticsEvent("shop_opened")
                .With("source", "main_menu")
                .With("coins", 250)
                .With("first_time", false);

            Assert.AreEqual("shop_opened", e.Name.Value);
            Assert.AreEqual("main_menu", e.Params["source"]);
            Assert.AreEqual(250L, e.Params["coins"]);
            Assert.AreEqual("false", e.Params["first_time"]);
        }
    }
}
