using LongLeap.Analytics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AnalyticsEventBaseTests
{
    private class TestEvent : AnalyticsEventBase
    {
        public TestEvent() : base(new EventName("test_event")) { }

        public void AddParam(string key, object value) => SetParam(key, value);
    }

    [SetUp]
    public void SetUp()
    {
        LogAssert.ignoreFailingMessages = false;
    }

    [Test]
    public void Constructor_ValidEvent_HasCorrectName()
    {
        var e = new TestEvent();
        Assert.AreEqual("test_event", e.EventName.Value);
    }

    [Test]
    public void Constructor_NoParams_HasEmptyDictionary()
    {
        var e = new TestEvent();
        Assert.IsEmpty(e.Params);
    }

    [Test]
    public void SetParam_ValidKey_StoresValue()
    {
        var e = new TestEvent();
        e.AddParam("hero_id", "ronin");
        Assert.AreEqual("ronin", e.Params["hero_id"]);
    }

    [Test]
    public void SetParam_MultipleParams_StoresAll()
    {
        var e = new TestEvent();
        e.AddParam("key1", "val1");
        e.AddParam("key2", 42);
        Assert.AreEqual(2, e.Params.Count);
        Assert.AreEqual("val1", e.Params["key1"]);
        Assert.AreEqual(42, e.Params["key2"]);
    }

    [Test]
    public void SetParam_EmptyKey_Skipped()
    {
        LogAssert.ignoreFailingMessages = true;
        var e = new TestEvent();
        e.AddParam("", "value");
        Assert.IsEmpty(e.Params);
    }

    [Test]
    public void SetParam_InvalidKeyCyrillic_Skipped()
    {
        LogAssert.ignoreFailingMessages = true;
        var e = new TestEvent();
        e.AddParam("ключ", "value");
        Assert.IsEmpty(e.Params);
    }

    [Test]
    public void SetParam_InvalidKeyWithSpace_Skipped()
    {
        LogAssert.ignoreFailingMessages = true;
        var e = new TestEvent();
        e.AddParam("my key", "value");
        Assert.IsEmpty(e.Params);
    }

    [Test]
    public void SetParam_StringValueLongerThan100_Truncated()
    {
        LogAssert.ignoreFailingMessages = true;
        var e = new TestEvent();
        var longStr = new string('x', 150);
        e.AddParam("key", longStr);
        Assert.AreEqual(100, ((string)e.Params["key"]).Length);
    }

    [Test]
    public void SetParam_StringValueExactly100_NotTruncated()
    {
        var e = new TestEvent();
        var exactStr = new string('x', 100);
        e.AddParam("key", exactStr);
        Assert.AreEqual(100, ((string)e.Params["key"]).Length);
    }

    [Test]
    public void SetParam_NonStringValueNotTruncated()
    {
        var e = new TestEvent();
        e.AddParam("key", 42);
        Assert.AreEqual(42, e.Params["key"]);
    }

    [Test]
    public void SetParam_OverwritesExistingKey()
    {
        var e = new TestEvent();
        e.AddParam("key", "first");
        e.AddParam("key", "second");
        Assert.AreEqual("second", e.Params["key"]);
        Assert.AreEqual(1, e.Params.Count);
    }

    [Test]
    public void SetParam_Exactly25Params_AllStored()
    {
        var e = new TestEvent();
        for (int i = 0; i < 25; i++)
            e.AddParam($"key{i}", i);
        Assert.AreEqual(25, e.Params.Count);
    }

    [Test]
    public void SetParam_26thParam_Skipped()
    {
        LogAssert.ignoreFailingMessages = true;
        var e = new TestEvent();
        for (int i = 0; i < 25; i++)
            e.AddParam($"key{i}", i);
        e.AddParam("key_extra", "extra");
        Assert.AreEqual(25, e.Params.Count);
        Assert.IsFalse(e.Params.ContainsKey("key_extra"));
    }

    [Test]
    public void SetParam_NullValue_Stored()
    {
        var e = new TestEvent();
        e.AddParam("key", null);
        Assert.IsNull(e.Params["key"]);
    }

    [Test]
    public void SetParam_IntValue_Stored()
    {
        var e = new TestEvent();
        e.AddParam("score", 1000);
        Assert.AreEqual(1000, e.Params["score"]);
    }

    [Test]
    public void SetParam_FloatValue_Stored()
    {
        var e = new TestEvent();
        e.AddParam("distance", 150.5f);
        Assert.AreEqual(150.5f, e.Params["distance"]);
    }

    [Test]
    public void SetParam_BoolValue_Stored()
    {
        var e = new TestEvent();
        e.AddParam("is_critical", true);
        Assert.IsTrue((bool)e.Params["is_critical"]);
    }

    [Test]
    public void TimestampMicros_DefaultIsZero()
    {
        var e = new TestEvent();
        Assert.AreEqual(0, e.TimestampMicros);
    }

    [Test]
    public void SessionId_DefaultIsZero()
    {
        var e = new TestEvent();
        Assert.AreEqual(0L, e.SessionId);
    }

    [Test]
    public void TimestampMicros_CanSet()
    {
        var e = new TestEvent();
        e.TimestampMicros = 1234567890123456L;
        Assert.AreEqual(1234567890123456L, e.TimestampMicros);
    }

    [Test]
    public void SessionId_CanSet()
    {
        var e = new TestEvent();
        e.SessionId = 1690000000000L;
        Assert.AreEqual(1690000000000L, e.SessionId);
    }
}
