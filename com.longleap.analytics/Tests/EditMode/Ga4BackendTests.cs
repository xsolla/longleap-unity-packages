using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Analytics.Tests
{
    public class Ga4BackendTests
    {
        private FakeGa4Config _config;
        private FakeTransport _transport;

        [SetUp]
        public void SetUp()
        {
            _config = new FakeGa4Config();
            _transport = new FakeTransport();
        }

        private Ga4Backend Create() => new Ga4Backend(_config, "client_1", _transport);

        // The fake transport completes synchronously, so the task is already done.
        private static SendResult Run(Ga4Backend backend, int eventCount)
        {
            var task = backend.SendAsync(Envelopes.Many(eventCount));
            Assert.IsTrue(task.IsCompleted);
            return task.Result;
        }

        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new Ga4Backend(null, "client", _transport));
            Assert.Throws<ArgumentException>(() => new Ga4Backend(new FakeGa4Config { MeasurementId = "" }, "client", _transport));
            Assert.Throws<ArgumentException>(() => new Ga4Backend(new FakeGa4Config { ApiSecret = null }, "client", _transport));
            Assert.Throws<ArgumentException>(() => new Ga4Backend(_config, "", _transport));
        }

        [Test]
        public void Send_Success_OneRequestPer25Events()
        {
            var result = Run(Create(), 30);

            Assert.IsTrue(result.IsComplete);
            Assert.AreEqual(2, _transport.Calls.Count);
            StringAssert.StartsWith("https://www.google-analytics.com/mp/collect?", _transport.Calls[0].Url);
        }

        [Test]
        public void Send_NetworkErrorOnSecondRequest_RetryLaterAfterFirst25()
        {
            _transport.Responses.Enqueue(new AnalyticsHttpResponse(204, "", null));
            _transport.Responses.Enqueue(AnalyticsHttpResponse.NetworkError("offline"));

            var result = Run(Create(), 30);

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(25, result.HandledCount);
        }

        [TestCase(408)]
        [TestCase(429)]
        [TestCase(500)]
        [TestCase(503)]
        public void Send_TransientStatus_RetryLater(long status)
        {
            _transport.Responses.Enqueue(new AnalyticsHttpResponse(status, "", "error"));

            var result = Run(Create(), 1);

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(0, result.HandledCount);
        }

        [Test]
        public void Send_PermanentRejection_DroppedAndBatchContinues()
        {
            LogAssert.Expect(LogType.Error, new Regex("^Ga4Backend: GA4 rejected a request"));
            _transport.Responses.Enqueue(new AnalyticsHttpResponse(400, "bad", "Bad Request"));

            var result = Run(Create(), 30);

            Assert.IsTrue(result.IsComplete);
            Assert.AreEqual(2, _transport.Calls.Count);
        }

        [Test]
        public void Send_TransportThrows_RetryLater()
        {
            _transport.Throw = true;

            var result = Run(Create(), 1);

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(0, result.HandledCount);
        }

        [Test]
        public void Send_ValidateOnly_UsesDebugEndpointAndLogsMessages()
        {
            _config.ValidateOnly = true;
            _transport.Responses.Enqueue(new AnalyticsHttpResponse(200,
                "{\"validationMessages\":[{\"fieldPath\":\"events\",\"description\":\"bad\",\"validationCode\":\"VALUE_INVALID\"}]}",
                null));
            LogAssert.Expect(LogType.Warning, new Regex("^Ga4Backend: validation: VALUE_INVALID"));

            var result = Run(Create(), 1);

            Assert.IsTrue(result.IsComplete);
            StringAssert.Contains("/debug/mp/collect?", _transport.Calls[0].Url);
        }

        [Test]
        public void Send_EmptyBatch_NoRequest()
        {
            var task = Create().SendAsync(Array.Empty<AnalyticsEnvelope>());

            Assert.IsTrue(task.Result.IsComplete);
            Assert.IsEmpty(_transport.Calls);
        }
    }
}
