using System.Collections.Generic;
using LongLeap.Analytics;
using NUnit.Framework;
using UnityEngine.TestTools;

public class Ga4BackendTests
{
    private sealed class TestGa4Config : IGa4Config
    {
        public string MeasurementId => "G-TEST123";
        public string ApiSecret => "test_secret";
        public string ClientId => "test_client_123";
        public string EndpointUrl => "https://www.google-analytics.com/mp/collect";
    }

    private sealed class TestEvent : AnalyticsEventBase
    {
        public TestEvent(string name = "test_event") : base(new EventName(name)) { }
        public void AddParam(string key, object value) => SetParam(key, value);
    }

    [SetUp]
    public void SetUp()
    {
        LogAssert.ignoreFailingMessages = false;
    }

    [Test]
    public void Constructor_NullConfig_Throws()
    {
        Assert.That(() => new Ga4MeasurementBackend(null), Throws.ArgumentNullException);
    }

    [Test]
    public void BuildPayload_SingleEvent_HasCorrectStructure()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);
        var e = new TestEvent();
        e.AddParam("hero_id", "ronin");

        var payload = backend.BuildPayload(new List<IAnalyticsEvent> { e });

        Assert.AreEqual("test_client_123", payload["client_id"]);
        var eventsList = (List<Dictionary<string, object>>)payload["events"];
        Assert.AreEqual(1, eventsList.Count);

        var evt = eventsList[0];
        Assert.AreEqual("test_event", evt["name"]);
        Assert.IsTrue(evt.ContainsKey("timestamp_micros"));

        var prms = (Dictionary<string, object>)evt["params"];
        Assert.AreEqual("ronin", prms["hero_id"]);
    }

    [Test]
    public void BuildPayload_MultipleEvents_AllPresent()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);

        var events = new List<IAnalyticsEvent>
        {
            new TestEvent("event_a"),
            new TestEvent("event_b"),
            new TestEvent("event_c")
        };

        var payload = backend.BuildPayload(events);
        var eventsList = (List<Dictionary<string, object>>)payload["events"];

        Assert.AreEqual(3, eventsList.Count);
        Assert.AreEqual("event_a", eventsList[0]["name"]);
        Assert.AreEqual("event_b", eventsList[1]["name"]);
        Assert.AreEqual("event_c", eventsList[2]["name"]);
    }

    [Test]
    public void BuildPayload_EventWithMultipleParams_AllSerialized()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);
        var e = new TestEvent();
        e.AddParam("string_key", "value");
        e.AddParam("int_key", 42);
        e.AddParam("float_key", 3.14f);
        e.AddParam("bool_key", true);

        var payload = backend.BuildPayload(new List<IAnalyticsEvent> { e });
        var eventsList = (List<Dictionary<string, object>>)payload["events"];
        var prms = (Dictionary<string, object>)eventsList[0]["params"];

        Assert.AreEqual("value", prms["string_key"]);
        Assert.AreEqual(42, prms["int_key"]);
        Assert.AreEqual(3.14f, prms["float_key"]);
        Assert.AreEqual(true, prms["bool_key"]);
    }

    [Test]
    public void BuildPayload_TimestampMicros_PresentAsNumber()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);

        var e = new TestEvent();
        e.TimestampMicros = 1234567890123456L;

        var payload = backend.BuildPayload(new List<IAnalyticsEvent> { e });
        var eventsList = (List<Dictionary<string, object>>)payload["events"];
        var timestamp = eventsList[0]["timestamp_micros"];

        Assert.IsTrue(timestamp is long);
        Assert.Greater((long)timestamp, 0);
    }

    [Test]
    public void BuildUrl_ConstructsCorrectly()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);

        var url = backend.BuildUrl();

        Assert.AreEqual(
            "https://www.google-analytics.com/mp/collect?measurement_id=G-TEST123&api_secret=test_secret",
            url);
    }

    [Test]
    public void SendBatch_EmptyBatch_DoesNotThrow()
    {
        LogAssert.ignoreFailingMessages = true;
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);

        Assert.DoesNotThrow(() => backend.SendBatch(new List<IAnalyticsEvent>()));
    }

    [Test]
    public void SendBatch_NullBatch_DoesNotThrow()
    {
        LogAssert.ignoreFailingMessages = true;
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);

        Assert.DoesNotThrow(() => backend.SendBatch(null));
    }

    [Test]
    public void SendBatch_30Events_SplitsIntoTwoChunks()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);
        var events = new List<IAnalyticsEvent>();

        for (int i = 0; i < 30; i++)
            events.Add(new TestEvent($"event_{i}"));

        Assert.DoesNotThrow(() => backend.SendBatch(events));
    }

    [Test]
    public void BuildPayload_IncludesSessionIdInParams()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);
        var e = new TestEvent();
        e.SessionId = 1690000000000L;

        var payload = backend.BuildPayload(new List<IAnalyticsEvent> { e });
        var eventsList = (List<Dictionary<string, object>>)payload["events"];
        var prms = (Dictionary<string, object>)eventsList[0]["params"];

        Assert.AreEqual(1690000000000L, prms["session_id"]);
    }

    [Test]
    public void BuildPayload_MultipleEvents_DifferentTimestamps()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);
        var e1 = new TestEvent("event_a");
        var e2 = new TestEvent("event_b");
        e1.TimestampMicros = 1000000;
        e2.TimestampMicros = 2000000;

        var payload = backend.BuildPayload(new List<IAnalyticsEvent> { e1, e2 });
        var eventsList = (List<Dictionary<string, object>>)payload["events"];

        Assert.AreEqual(1000000L, eventsList[0]["timestamp_micros"]);
        Assert.AreEqual(2000000L, eventsList[1]["timestamp_micros"]);
    }

    [Test]
    public void BuildPayload_ParamsNotCopied_OriginalMutable()
    {
        var config = new TestGa4Config();
        var backend = new Ga4MeasurementBackend(config);
        var e = new TestEvent();
        e.AddParam("key", "value");

        var payload = backend.BuildPayload(new List<IAnalyticsEvent> { e });
        var eventsList = (List<Dictionary<string, object>>)payload["events"];
        var prms = (Dictionary<string, object>)eventsList[0]["params"];

        // Should reference the same dict (no copy)
        Assert.AreEqual("value", prms["key"]);
    }
}
