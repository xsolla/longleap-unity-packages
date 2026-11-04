using System.Collections.Generic;
using NUnit.Framework;

namespace LongLeap.Analytics.Tests
{
    public class AnalyticsEnvelopeTests
    {
        private static AnalyticsEnvelope Build(Dictionary<string, object> parameters) =>
            new AnalyticsEnvelope(new EventName("test_event"), parameters, 1L, 2L);

        [Test]
        public void Constructor_CopiesParams_SourceChangesDoNotLeak()
        {
            var source = new Dictionary<string, object> { ["level"] = 1 };
            var envelope = Build(source);

            source["level"] = 2;
            source["extra"] = "x";

            Assert.AreEqual(1L, envelope.Params["level"]);
            Assert.IsFalse(envelope.Params.ContainsKey("extra"));
        }

        [Test]
        public void Constructor_NormalizesValues()
        {
            var envelope = Build(new Dictionary<string, object>
            {
                ["i"] = 5,
                ["f"] = 2.5f,
                ["b"] = true,
                ["s"] = new string('y', 120),
            });

            Assert.AreEqual(5L, envelope.Params["i"]);
            Assert.AreEqual(2.5d, envelope.Params["f"]);
            Assert.AreEqual("true", envelope.Params["b"]);
            Assert.AreEqual(Ga4Rules.MaxParamValueLength, ((string)envelope.Params["s"]).Length);
        }

        [Test]
        public void Constructor_DropsInvalidKeysServiceParamsAndUnsupportedValues()
        {
            var envelope = Build(new Dictionary<string, object>
            {
                ["ok"] = "fine",
                ["bad key"] = "x",
                ["session_id"] = 1,
                ["null_value"] = null,
                ["nested"] = new List<int> { 1 },
                ["nan"] = double.NaN,
            });

            CollectionAssert.AreEquivalent(new[] { "ok" }, envelope.Params.Keys);
        }

        [Test]
        public void Constructor_CapsAtMaxCustomParams()
        {
            var source = new Dictionary<string, object>();
            for (var i = 0; i < Ga4Rules.MaxParamsPerEvent; i++)
                source[$"key{i}"] = i;

            var envelope = Build(source);

            Assert.AreEqual(Ga4Rules.MaxCustomParams, envelope.Params.Count);
        }

        [Test]
        public void Constructor_NullParams_Empty()
        {
            Assert.IsEmpty(Build(null).Params);
        }

        [Test]
        public void TrySetParam_DoesNotOverwriteEventParam()
        {
            var envelope = Build(new Dictionary<string, object> { ["key"] = "event" });

            Assert.IsFalse(envelope.TrySetParam("key", "enricher"));
            Assert.AreEqual("event", envelope.Params["key"]);
        }

        [Test]
        public void TrySetParam_RefusesServiceParamsAndInvalidKeys()
        {
            var envelope = Build(null);

            Assert.IsFalse(envelope.TrySetParam("session_id", 1));
            Assert.IsFalse(envelope.TrySetParam("engagement_time_msec", 1));
            Assert.IsFalse(envelope.TrySetParam("bad key", 1));
            Assert.IsEmpty(envelope.Params);
        }

        [Test]
        public void TrySetParam_StopsAtMaxParamsPerEvent()
        {
            var envelope = Build(null);

            for (var i = 0; i < Ga4Rules.MaxParamsPerEvent; i++)
                Assert.IsTrue(envelope.TrySetParam($"key{i}", i));

            Assert.IsFalse(envelope.TrySetParam("one_more", 1));
            Assert.AreEqual(Ga4Rules.MaxParamsPerEvent, envelope.Params.Count);
        }

        [Test]
        public void Constructor_EmptyUserId_IsNull_AndUserPropertiesNeverNull()
        {
            var envelope = new AnalyticsEnvelope(new EventName("test_event"), null, 1L, 2L, string.Empty);

            Assert.IsNull(envelope.UserId);
            Assert.IsNotNull(envelope.UserProperties);
            Assert.IsEmpty(envelope.UserProperties);
        }
    }
}
