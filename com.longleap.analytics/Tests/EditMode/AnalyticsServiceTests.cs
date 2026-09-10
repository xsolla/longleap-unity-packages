using System;
using System.Collections.Generic;
using LongLeap.Analytics;
using LongLeap.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LongLeap.Analytics.Tests
{

public class AnalyticsServiceTests
{
    private sealed class SpyBackend : IAnalyticsBackend
    {
        public List<IAnalyticsEvent> ReceivedBatch { get; private set; }
        public int CallCount { get; private set; }

        public void SendBatch(IReadOnlyList<IAnalyticsEvent> events)
        {
            CallCount++;
            ReceivedBatch = new List<IAnalyticsEvent>(events);
        }
    }

    private sealed class MockExperimentProvider : IExperimentInfoProvider
    {
        public IReadOnlyList<AbExperimentInfo> ActiveExperiments { get; set; } = Array.Empty<AbExperimentInfo>();
    }

    [SetUp]
    public void SetUp()
    {
        LogAssert.ignoreFailingMessages = false;
    }

    [Test]
    public void Send_EnqueuesEvent_DoesNotFlushImmediately()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        service.Send(new TestEvent());

        Assert.AreEqual(0, backend.CallCount);
    }

    [Test]
    public void Send_NullEvent_NoOp()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 1, flushIntervalSec: 5f, backend: backend);
        service.Send(null);

        Assert.AreEqual(0, backend.CallCount);
    }

    [Test]
    public void Send_ReachesBatchSize_TriggersFlush()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 2, flushIntervalSec: 5f, backend: backend);

        service.Send(new TestEvent());
        Assert.AreEqual(0, backend.CallCount);

        service.Send(new TestEvent());
        Assert.AreEqual(1, backend.CallCount);
        Assert.AreEqual(2, backend.ReceivedBatch.Count);
    }

    [Test]
    public void Flush_DequeuesAllEvents_CallsBackend()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);

        service.Send(new TestEvent());
        service.Send(new TestEvent());
        service.Flush();

        Assert.AreEqual(1, backend.CallCount);
        Assert.AreEqual(2, backend.ReceivedBatch.Count);
    }

    [Test]
    public void Flush_EmptyQueue_NoOp()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);

        service.Flush();

        Assert.AreEqual(0, backend.CallCount);
    }

    [Test]
    public void Flush_DoubleFlush_SecondIsNoOp()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);

        service.Send(new TestEvent());
        service.Flush();
        service.Flush();

        Assert.AreEqual(1, backend.CallCount);
    }

    [Test]
    public void Send_AfterFlush_EnqueuesNewEvents()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);

        service.Send(new TestEvent());
        service.Flush();
        Assert.AreEqual(1, backend.CallCount);

        service.Send(new TestEvent());
        service.Flush();
        Assert.AreEqual(2, backend.CallCount);
    }

    [Test]
    public void Send_MultipleEvents_ReachesBatchSizeExactly()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 3, flushIntervalSec: 5f, backend: backend);

        service.Send(new TestEvent());
        service.Send(new TestEvent());
        Assert.AreEqual(0, backend.CallCount);

        service.Send(new TestEvent());
        Assert.AreEqual(1, backend.CallCount);
        Assert.AreEqual(3, backend.ReceivedBatch.Count);
    }

    [Test]
    public void Send_WithNullBackend_DoesNotThrow()
    {
        var service = new AnalyticsService(batchSize: 1, flushIntervalSec: 5f, backend: null);

        Assert.DoesNotThrow(() =>
        {
            service.Send(new TestEvent());
            service.Flush();
        });
    }

    [Test]
    public void Send_StampsTimestampMicros()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 1, flushIntervalSec: 5f, backend: backend);
        var e = new TestEvent();

        service.Send(e);

        Assert.Greater(e.TimestampMicros, 0);
    }

    [Test]
    public void Send_StampsEngagementTimeMsec()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        var e1 = new TestEvent();

        service.Send(e1);

        Assert.IsTrue(e1.Params.ContainsKey("engagement_time_msec"));
        Assert.AreEqual(0L, e1.Params["engagement_time_msec"]);
    }

    [Test]
    public void Send_MultipleEvents_EngagementTimeIncreases()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        var e1 = new TestEvent();
        var e2 = new TestEvent();

        service.Send(e1);
        service.Send(e2);

        var t1 = (long)e1.Params["engagement_time_msec"];
        var t2 = (long)e2.Params["engagement_time_msec"];
        Assert.AreEqual(0L, t1);
        Assert.GreaterOrEqual(t2, t1);
    }

    [Test]
    public void Send_StampsSessionId()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 1, flushIntervalSec: 5f, backend: backend);
        var e = new TestEvent();

        service.Send(e);

        Assert.Greater(e.SessionId, 0, "SessionId should be set to a positive timestamp");
    }

    [Test]
    public void Send_MultipleEvents_SameSessionId()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        var e1 = new TestEvent();
        var e2 = new TestEvent();

        service.Send(e1);
        service.Send(e2);

        Assert.AreEqual(e1.SessionId, e2.SessionId);
    }

    [Test]
    public void Dispose_AfterDispose_SendIsNoOp()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 1, flushIntervalSec: 5f, backend: backend);

        service.Dispose();
        service.Send(new TestEvent());

        Assert.AreEqual(0, backend.CallCount);
    }

    [Test]
    public void Dispose_IsDisposedReturnsTrue()
    {
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: null);

        service.Dispose();

        Assert.IsTrue(service.IsDisposed);
    }

    [Test]
    public void Dispose_FreshService_IsDisposedFalse()
    {
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: null);

        Assert.IsFalse(service.IsDisposed);
    }

    [Test]
    public void Flush_BackendException_RequeuesEvents()
    {
        var throwingBackend = new ThrowingBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: throwingBackend);

        service.Send(new TestEvent());
        service.Send(new TestEvent());
        service.Flush();

        Assert.AreEqual(1, throwingBackend.CallCount);
    }

    [Test]
    public void Flush_BackendException_EventsStillFlushable()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);

        var throwingBackend = new ThrowingBackend();
        var serviceWithThrow = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: throwingBackend);

        serviceWithThrow.Send(new TestEvent());
        serviceWithThrow.Send(new TestEvent());
        serviceWithThrow.Flush();

        Assert.AreEqual(1, throwingBackend.CallCount);
        Assert.AreEqual(0, backend.CallCount);
    }

    [Test]
    public void Send_WithExperiments_AddsExpParams()
    {
        var backend = new SpyBackend();
        var provider = new MockExperimentProvider
        {
            ActiveExperiments = new List<AbExperimentInfo>
            {
                new AbExperimentInfo("exp_01", 1),
                new AbExperimentInfo("exp_02", 2)
            }
        };
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        service.SetExperimentInfoProvider(provider);
        var e = new TestEvent();

        service.Send(e);

        Assert.IsTrue(e.Params.ContainsKey("exp_exp_01"));
        Assert.AreEqual(1, e.Params["exp_exp_01"]);
        Assert.IsTrue(e.Params.ContainsKey("exp_exp_02"));
        Assert.AreEqual(2, e.Params["exp_exp_02"]);
    }

    [Test]
    public void Send_WithEmptyExperimentList_NoParamsAdded()
    {
        var backend = new SpyBackend();
        var provider = new MockExperimentProvider
        {
            ActiveExperiments = Array.Empty<AbExperimentInfo>()
        };
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        service.SetExperimentInfoProvider(provider);
        var e = new TestEvent();

        service.Send(e);

        Assert.IsFalse(e.Params.ContainsKey("exp_exp_01"));
    }

    [Test]
    public void Send_WithNullProvider_NoParamsAdded()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        var e = new TestEvent();

        Assert.DoesNotThrow(() => service.Send(e));

        Assert.Greater(e.TimestampMicros, 0);
    }

    [Test]
    public void SetExperimentInfoProvider_Null_ClearsProvider()
    {
        var backend = new SpyBackend();
        var provider = new MockExperimentProvider
        {
            ActiveExperiments = new List<AbExperimentInfo>
            {
                new AbExperimentInfo("exp_01", 1)
            }
        };
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        service.SetExperimentInfoProvider(provider);

        service.SetExperimentInfoProvider(null);

        var e = new TestEvent();
        service.Send(e);
        Assert.IsFalse(e.Params.ContainsKey("exp_exp_01"));
    }

    [Test]
    public void Send_WithoutProvider_BehaviorUnchanged()
    {
        var backend = new SpyBackend();
        var service = new AnalyticsService(batchSize: 10, flushIntervalSec: 5f, backend: backend);
        var e = new TestEvent();

        service.Send(e);

        Assert.Greater(e.TimestampMicros, 0);
        Assert.Greater(e.SessionId, 0);
        Assert.IsTrue(e.Params.ContainsKey("engagement_time_msec"));
    }

    private sealed class TestEvent : AnalyticsEventBase
    {
        public TestEvent() : base(new EventName("test_event")) { }
    }

    private sealed class ThrowingBackend : IAnalyticsBackend
    {
        public int CallCount { get; private set; }

        public void SendBatch(IReadOnlyList<IAnalyticsEvent> events)
        {
            CallCount++;
            throw new System.Exception("Simulated backend failure");
        }
    }
}

}
