using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Analytics.Tests
{
    public class AnalyticsServiceTests
    {
        private ManualClock _clock;
        private SpyBackend _backend;
        private SynchronizationContext _previousContext;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _backend = new SpyBackend();

            // Without a context, the continuation of a pending delivery runs
            // inline when the test completes it, so in-flight paths are
            // deterministic.
            _previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
        }

        [TearDown]
        public void TearDown()
        {
            SynchronizationContext.SetSynchronizationContext(_previousContext);
        }

        private AnalyticsService Create(Action<AnalyticsOptions> configure = null)
        {
            var options = new AnalyticsOptions { Clock = () => _clock.Now };
            configure?.Invoke(options);
            return new AnalyticsService(_backend, options);
        }

        private static AnalyticsEvent Event(string name = "test_event") => new AnalyticsEvent(name);

        private static string[] Names(System.Collections.Generic.IEnumerable<AnalyticsEnvelope> batch) =>
            batch.Select(e => e.Name.Value).ToArray();

        // --- construction ---

        [Test]
        public void Constructor_NullBackend_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AnalyticsService(null));
        }

        [Test]
        public void Constructor_InvalidOptions_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(o => o.BatchSize = 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(o => o.FlushIntervalSeconds = 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(o => { o.BatchSize = 10; o.MaxQueueSize = 5; }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(o => o.MaxAttempts = 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(o => o.SessionTimeout = TimeSpan.Zero));
        }

        // --- batching ---

        [Test]
        public void Send_BelowBatchSize_DoesNotFlush()
        {
            var service = Create();

            service.Send(Event());

            Assert.AreEqual(0, _backend.CallCount);
            Assert.AreEqual(1, service.PendingCount);
        }

        [Test]
        public void Send_ReachingBatchSize_FlushesWholeQueue()
        {
            var service = Create(o => o.BatchSize = 2);

            service.Send(Event("event_a"));
            service.Send(Event("event_b"));

            Assert.AreEqual(1, _backend.CallCount);
            CollectionAssert.AreEqual(new[] { "event_a", "event_b" }, Names(_backend.LastBatch));
            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void Flush_EmptyQueue_DoesNotCallBackend()
        {
            var service = Create();

            service.Flush();

            Assert.AreEqual(0, _backend.CallCount);
        }

        [Test]
        public void Flush_WhileInFlight_DoesNotStartSecondDelivery()
        {
            _backend.Pending = new TaskCompletionSource<SendResult>();
            var service = Create();

            service.Send(Event("event_a"));
            var first = service.FlushAsync();
            service.Send(Event("event_b"));
            var second = service.FlushAsync();

            Assert.AreEqual(1, _backend.CallCount);
            Assert.AreSame(first, second);
            Assert.AreEqual(1, service.PendingCount);
        }

        [Test]
        public void Update_FlushesAfterInterval()
        {
            var service = Create(o => o.FlushIntervalSeconds = 5f);
            service.Send(Event());

            service.Update(4.9f);
            Assert.AreEqual(0, _backend.CallCount);

            service.Update(0.2f);
            Assert.AreEqual(1, _backend.CallCount);
        }

        [Test]
        public void Update_EmptyQueue_DoesNotCallBackend()
        {
            var service = Create(o => o.FlushIntervalSeconds = 1f);

            service.Update(10f);

            Assert.AreEqual(0, _backend.CallCount);
        }

        // --- event snapshot ---

        [Test]
        public void Send_SnapshotsEvent_LaterChangesDoNotAffectQueued()
        {
            var service = Create();
            var e = Event().With("level", 1);

            service.Send(e);
            e.With("level", 2);
            service.Send(e);
            service.Flush();

            Assert.AreEqual(1L, _backend.LastBatch[0].Params["level"]);
            Assert.AreEqual(2L, _backend.LastBatch[1].Params["level"]);
        }

        [Test]
        public void Send_DoesNotMutateEvent()
        {
            var service = Create();
            var e = Event().With("level", 1);

            service.Send(e);

            CollectionAssert.AreEquivalent(new[] { "level" }, e.Params.Keys);
        }

        [Test]
        public void Send_NullEvent_Ignored()
        {
            var service = Create();

            service.Send(null);

            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void Send_EventWithoutValidName_Dropped()
        {
            var service = Create();

            service.Send(new AnalyticsEvent(EventName.None));

            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void Send_StampsTimestampFromClock()
        {
            var service = Create();

            service.Send(Event());
            service.Flush();

            Assert.AreEqual(_clock.NowMicros, _backend.LastBatch[0].TimestampMicros);
        }

        // --- session ---

        [Test]
        public void SessionId_IsUnixSecondsAtStart_AndStampedOnEvents()
        {
            var expected = _clock.Now.ToUnixTimeSeconds();
            var service = Create();

            service.Send(Event());
            service.Flush();

            var envelope = _backend.LastBatch[0];
            Assert.AreEqual(expected, service.SessionId);
            Assert.AreEqual(expected, envelope.SessionId);
            Assert.AreEqual(expected, envelope.Params[Ga4Rules.SessionIdParam]);
        }

        [Test]
        public void Session_ActivityWithinTimeout_KeepsSession()
        {
            var service = Create();
            var initial = service.SessionId;

            _clock.Advance(TimeSpan.FromMinutes(20));
            service.Send(Event());
            _clock.Advance(TimeSpan.FromMinutes(20));
            service.Send(Event());

            Assert.AreEqual(initial, service.SessionId);
        }

        [Test]
        public void Session_InactivityBeyondTimeout_StartsNewSession()
        {
            var service = Create();
            var initial = service.SessionId;

            _clock.Advance(TimeSpan.FromMinutes(31));
            service.Send(Event());

            Assert.Greater(service.SessionId, initial);
        }

        [Test]
        public void Session_LongBackground_StartsNewSessionOnReturn()
        {
            var service = Create();
            var initial = service.SessionId;

            service.SetForeground(false);
            _clock.Advance(TimeSpan.FromHours(1));
            service.SetForeground(true);

            Assert.Greater(service.SessionId, initial);
        }

        [Test]
        public void Session_ShortBackground_KeepsSession()
        {
            var service = Create();
            var initial = service.SessionId;

            service.SetForeground(false);
            _clock.Advance(TimeSpan.FromMinutes(5));
            service.SetForeground(true);

            Assert.AreEqual(initial, service.SessionId);
        }

        // --- engagement ---

        [Test]
        public void Engagement_IsForegroundTimeSincePreviousEvent()
        {
            var service = Create();

            _clock.AdvanceSeconds(2);
            service.Send(Event());
            _clock.AdvanceSeconds(3);
            service.Send(Event());
            service.Flush();

            Assert.AreEqual(2000L, _backend.LastBatch[0].Params[Ga4Rules.EngagementTimeParam]);
            Assert.AreEqual(3000L, _backend.LastBatch[1].Params[Ga4Rules.EngagementTimeParam]);
        }

        [Test]
        public void Engagement_IsAtLeastOneMillisecond()
        {
            var service = Create();

            service.Send(Event());
            service.Flush();

            Assert.AreEqual(1L, _backend.LastBatch[0].Params[Ga4Rules.EngagementTimeParam]);
        }

        [Test]
        public void Engagement_ExcludesBackgroundTime()
        {
            var service = Create();

            _clock.AdvanceSeconds(2);
            service.SetForeground(false);
            _clock.AdvanceSeconds(60);
            service.SetForeground(true);
            _clock.AdvanceSeconds(1);
            service.Send(Event());
            service.Flush();

            Assert.AreEqual(3000L, _backend.LastBatch[0].Params[Ga4Rules.EngagementTimeParam]);
        }

        [Test]
        public void SetForeground_False_FlushesQueue()
        {
            var service = Create();
            service.Send(Event());

            service.SetForeground(false);

            Assert.AreEqual(1, _backend.CallCount);
        }

        // --- failures and retry ---

        [Test]
        public void RetryLater_RequeuesUnhandledEventsFirst_InOrder()
        {
            _backend.Results.Enqueue(SendResult.RetryLater(1, "offline"));
            var service = Create();

            service.Send(Event("event_a"));
            service.Send(Event("event_b"));
            service.Send(Event("event_c"));
            service.Flush();
            service.Send(Event("event_d"));
            service.Flush();

            Assert.AreEqual(2, _backend.CallCount);
            CollectionAssert.AreEqual(new[] { "event_b", "event_c", "event_d" }, Names(_backend.LastBatch));
        }

        [Test]
        public void RetryLater_BacksOffAutomaticFlushes()
        {
            _backend.Results.Enqueue(SendResult.RetryLater(0, "offline"));
            var service = Create(o => { o.BatchSize = 1; o.FlushIntervalSeconds = 5f; });

            service.Send(Event("event_a"));      // flush #1 fails -> 5 s backoff
            service.Send(Event("event_b"));      // batch size reached, but backing off
            Assert.AreEqual(1, _backend.CallCount);

            service.Update(4.9f);
            Assert.AreEqual(1, _backend.CallCount);

            service.Update(0.2f);                // backoff over -> retry
            Assert.AreEqual(2, _backend.CallCount);
            CollectionAssert.AreEqual(new[] { "event_a", "event_b" }, Names(_backend.LastBatch));
        }

        [Test]
        public void RetryLater_BackoffGrowsExponentially()
        {
            _backend.AlwaysResult = SendResult.RetryLater(0, "offline");
            var service = Create(o => { o.FlushIntervalSeconds = 1f; o.MaxAttempts = 10; });
            service.Send(Event());

            service.Flush();                     // failure 1 -> 1 s
            service.Update(1.01f);               // failure 2 -> 2 s
            Assert.AreEqual(2, _backend.CallCount);

            service.Update(1.5f);
            Assert.AreEqual(2, _backend.CallCount);

            service.Update(0.6f);                // failure 3
            Assert.AreEqual(3, _backend.CallCount);
        }

        [Test]
        public void BackendThrows_EventsRequeued()
        {
            _backend.Throw = true;
            var service = Create();
            service.Send(Event());
            service.Send(Event());

            service.Flush();

            Assert.AreEqual(1, _backend.CallCount);
            Assert.AreEqual(2, service.PendingCount);
        }

        [Test]
        public void Event_DroppedAfterMaxAttempts()
        {
            _backend.AlwaysResult = SendResult.RetryLater(0, "server error", attemptedCount: 1);
            var service = Create(o => o.MaxAttempts = 2);
            service.Send(Event());

            service.Flush();
            Assert.AreEqual(1, service.PendingCount);

            service.Flush();
            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void Queue_WhenFull_DropsOldest()
        {
            _backend.AlwaysResult = SendResult.RetryLater(0, "offline");
            var service = Create(o => { o.BatchSize = 2; o.MaxQueueSize = 3; o.MaxAttempts = 10; });

            service.Send(Event("event_1"));
            service.Send(Event("event_2"));      // flush fails -> backoff, both requeued
            service.Send(Event("event_3"));
            service.Send(Event("event_4"));      // drops event_1
            service.Send(Event("event_5"));      // drops event_2

            Assert.AreEqual(3, service.PendingCount);
            service.Flush();
            CollectionAssert.AreEqual(new[] { "event_3", "event_4", "event_5" }, Names(_backend.LastBatch));
        }

        [Test]
        public void NetworkFailures_DoNotUseUpAttempts()
        {
            _backend.AlwaysResult = SendResult.RetryLater(0, "offline");
            var service = Create(o => o.MaxAttempts = 2);
            service.Send(Event());

            for (var i = 0; i < 10; i++)
                service.Flush();

            Assert.AreEqual(10, _backend.CallCount);
            Assert.AreEqual(1, service.PendingCount);
        }

        [Test]
        public void Attempts_ChargedOnlyForAttemptedEvents()
        {
            _backend.Results.Enqueue(SendResult.RetryLater(1, "server error", attemptedCount: 1));
            var service = Create(o => o.MaxAttempts = 1);
            service.Send(Event("event_a"));
            service.Send(Event("event_b"));
            service.Send(Event("event_c"));

            service.Flush();   // a handled, b charged and dropped, c never tried
            service.Flush();

            CollectionAssert.AreEqual(new[] { "event_c" }, Names(_backend.LastBatch));
        }

        [Test]
        public void Events_OlderThanMaxEventAge_DroppedBeforeSending()
        {
            var service = Create(o => o.MaxEventAge = TimeSpan.FromHours(1));
            service.Send(Event("event_old"));
            _clock.Advance(TimeSpan.FromHours(2));
            service.Send(Event("event_new"));

            service.Flush();

            CollectionAssert.AreEqual(new[] { "event_new" }, Names(_backend.LastBatch));
        }

        // --- in-flight deliveries ---

        [Test]
        public void FlushDuringDelivery_RunsAfterSuccess_AndTaskWaitsForIt()
        {
            var pending = new TaskCompletionSource<SendResult>();
            _backend.Pending = pending;
            var service = Create();

            service.Send(Event("event_a"));
            var task = service.FlushAsync();
            service.Send(Event("event_b"));
            service.Flush();

            Assert.IsFalse(task.IsCompleted);

            _backend.Pending = null;
            pending.SetResult(SendResult.Complete);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(2, _backend.CallCount);
            CollectionAssert.AreEqual(new[] { "event_b" }, Names(_backend.LastBatch));
            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void FlushDuringDelivery_SkippedAfterFailure_BackoffApplies()
        {
            var pending = new TaskCompletionSource<SendResult>();
            _backend.Pending = pending;
            var service = Create();

            service.Send(Event("event_a"));
            var task = service.FlushAsync();
            service.Send(Event("event_b"));
            service.Flush();

            _backend.Pending = null;
            pending.SetResult(SendResult.RetryLater(0, "offline"));

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(1, _backend.CallCount);
            Assert.AreEqual(2, service.PendingCount);
        }

        [Test]
        public void DisposeDuringDelivery_SendsTheRestAfterSuccess()
        {
            var pending = new TaskCompletionSource<SendResult>();
            _backend.Pending = pending;
            var service = Create();

            service.Send(Event("event_a"));
            service.Flush();
            service.Send(Event("event_b"));
            service.Dispose();

            _backend.Pending = null;
            pending.SetResult(SendResult.Complete);

            Assert.AreEqual(2, _backend.CallCount);
            CollectionAssert.AreEqual(new[] { "event_b" }, Names(_backend.LastBatch));
        }

        [Test]
        public void DisposeDuringDelivery_FailedBatchNotRequeued()
        {
            var pending = new TaskCompletionSource<SendResult>();
            _backend.Pending = pending;
            var service = Create();

            service.Send(Event("event_a"));
            service.Flush();
            service.Dispose();

            _backend.Pending = null;
            pending.SetResult(SendResult.RetryLater(0, "offline"));

            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void ConsentWithdrawnAndGivenDuringDelivery_FailedBatchDiscarded()
        {
            var pending = new TaskCompletionSource<SendResult>();
            _backend.Pending = pending;
            var service = Create();

            service.Send(Event("event_a"));
            service.Flush();
            service.SetCollectionEnabled(false);
            service.SetCollectionEnabled(true);

            _backend.Pending = null;
            pending.SetResult(SendResult.RetryLater(0, "offline"));

            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void BackendThatSendsEvents_RunIsBounded()
        {
            AnalyticsService service = null;
            service = Create(o => o.BatchSize = 1);
            _backend.OnSend = _ => service.Send(Event("event_loop"));

            service.Send(Event("event_a"));

            Assert.LessOrEqual(_backend.CallCount, 4);
            Assert.AreEqual(1, service.PendingCount);
        }

        // --- background event ---

        [Test]
        public void BackgroundEvent_SentWithEngagementOnBackground()
        {
            var service = Create(o => o.BackgroundEvent = new EventName("app_paused"));

            _clock.AdvanceSeconds(2);
            service.SetForeground(false);

            var envelope = _backend.LastBatch[0];
            Assert.AreEqual("app_paused", envelope.Name.Value);
            Assert.AreEqual(2000L, envelope.Params[Ga4Rules.EngagementTimeParam]);
        }

        [Test]
        public void BackgroundEvent_NotConfigured_NothingSent()
        {
            var service = Create();

            _clock.AdvanceSeconds(2);
            service.SetForeground(false);

            Assert.AreEqual(0, _backend.CallCount);
        }

        // --- consent ---

        [Test]
        public void EnablingCollection_ResetsEngagementBaseline()
        {
            var service = Create(o => o.CollectionEnabled = false);
            _clock.AdvanceSeconds(10);

            service.SetCollectionEnabled(true);
            _clock.AdvanceSeconds(1);
            service.Send(Event());
            service.Flush();

            Assert.AreEqual(1000L, _backend.LastBatch[0].Params[Ga4Rules.EngagementTimeParam]);
        }

        [Test]
        public void CollectionDisabledInOptions_SendIgnored()
        {
            var service = Create(o => o.CollectionEnabled = false);

            service.Send(Event());

            Assert.IsFalse(service.IsCollectionEnabled);
            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void SetCollectionEnabled_False_ClearsQueue_TrueResumes()
        {
            var service = Create();
            service.Send(Event());

            service.SetCollectionEnabled(false);
            Assert.AreEqual(0, service.PendingCount);

            service.Send(Event());
            Assert.AreEqual(0, service.PendingCount);

            service.SetCollectionEnabled(true);
            service.Send(Event());
            Assert.AreEqual(1, service.PendingCount);
        }

        // --- user ---

        [Test]
        public void SetUserId_StampedOnLaterEvents_NullClears()
        {
            var service = Create();

            service.Send(Event("event_a"));
            service.SetUserId("player_42");
            service.Send(Event("event_b"));
            service.SetUserId(null);
            service.Send(Event("event_c"));
            service.Flush();

            Assert.IsNull(_backend.LastBatch[0].UserId);
            Assert.AreEqual("player_42", _backend.LastBatch[1].UserId);
            Assert.IsNull(_backend.LastBatch[2].UserId);
        }

        [Test]
        public void SetUserId_TooLong_Ignored()
        {
            var service = Create();

            service.SetUserId(new string('u', Ga4Rules.MaxUserIdLength + 1));
            service.Send(Event());
            service.Flush();

            Assert.IsNull(_backend.LastBatch[0].UserId);
        }

        [Test]
        public void SetUserProperty_SnapshotPerChange()
        {
            var service = Create();

            service.SetUserProperty("tier", "gold");
            service.Send(Event("event_a"));
            service.Send(Event("event_b"));
            service.SetUserProperty("tier", "platinum_long_value_that_exceeds_36_chars");
            service.Send(Event("event_c"));
            service.SetUserProperty("tier", null);
            service.Send(Event("event_d"));
            service.Flush();

            var batch = _backend.LastBatch;
            Assert.AreEqual("gold", batch[0].UserProperties["tier"]);
            Assert.AreSame(batch[0].UserProperties, batch[1].UserProperties);
            Assert.AreEqual(Ga4Rules.MaxUserPropertyValueLength, batch[2].UserProperties["tier"].Length);
            Assert.IsEmpty(batch[3].UserProperties);
        }

        [TestCase("user_id")]
        [TestCase("first_open_time")]
        [TestCase("bad name")]
        [TestCase("a_name_longer_than_24_chars")]
        public void SetUserProperty_InvalidName_Ignored(string name)
        {
            var service = Create();

            service.SetUserProperty(name, "x");
            service.Send(Event());
            service.Flush();

            Assert.IsEmpty(_backend.LastBatch[0].UserProperties);
        }

        // --- enrichers ---

        private sealed class ConstantEnricher : IAnalyticsEnricher
        {
            public void Enrich(AnalyticsEnvelope envelope) => envelope.TrySetParam("build", "1.2.3");
        }

        private sealed class ThrowingEnricher : IAnalyticsEnricher
        {
            public void Enrich(AnalyticsEnvelope envelope) => throw new InvalidOperationException("enricher bug");
        }

        [Test]
        public void Enricher_AddsParams_RemoveStops()
        {
            var service = Create();
            var enricher = new ConstantEnricher();

            service.AddEnricher(enricher);
            service.Send(Event("event_a"));
            service.RemoveEnricher(enricher);
            service.Send(Event("event_b"));
            service.Flush();

            Assert.AreEqual("1.2.3", _backend.LastBatch[0].Params["build"]);
            Assert.IsFalse(_backend.LastBatch[1].Params.ContainsKey("build"));
        }

        [Test]
        public void Enricher_Throwing_IsLogged_EventStillSent()
        {
            LogAssert.Expect(LogType.Exception, new Regex("enricher bug"));
            var service = Create();
            service.AddEnricher(new ThrowingEnricher());

            service.Send(Event());

            Assert.AreEqual(1, service.PendingCount);
        }

        // --- dispose ---

        [Test]
        public void Dispose_FlushesQueue_ThenIgnoresSend()
        {
            var service = Create();
            service.Send(Event());

            service.Dispose();
            service.Send(Event());

            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(1, _backend.CallCount);
            Assert.AreEqual(1, _backend.LastBatch.Count);
        }

        [Test]
        public void Dispose_Twice_IsSafe()
        {
            var service = Create();

            service.Dispose();

            Assert.DoesNotThrow(service.Dispose);
        }
    }
}
