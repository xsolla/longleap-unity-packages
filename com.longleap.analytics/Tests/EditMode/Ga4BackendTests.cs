using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Analytics.Tests
{
    public class Ga4BackendTests
    {
        private Ga4Settings _settings;
        private FakeTransport _transport;

        [SetUp]
        public void SetUp()
        {
            _settings = TestSettings.Create();
            _transport = new FakeTransport();
        }

        private Ga4Backend Create() => new Ga4Backend(_settings, "client_1", _transport);

        // The fake transport completes synchronously, so the task is already done.
        private static SendResult Run(Ga4Backend backend, int eventCount)
        {
            var task = backend.SendAsync(Envelopes.Many(eventCount));
            Assert.IsTrue(task.IsCompleted);
            return task.Result;
        }

        [Test]
        public void Settings_EmptyCredentials_Throw()
        {
            Assert.Throws<ArgumentException>(() => new Ga4Settings("", "secret"));
            Assert.Throws<ArgumentException>(() => new Ga4Settings("G-1", " "));
            Assert.Throws<ArgumentException>(() => new Ga4Settings(null, "secret"));
        }

        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new Ga4Backend(null, "client", _transport));
            Assert.Throws<ArgumentException>(() => new Ga4Backend(_settings, "", _transport));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create().MaxRequestBytes = 0);
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
        public void Send_NetworkError_RetryLater_NothingCharged()
        {
            _transport.Responses.Enqueue(new AnalyticsHttpResponse(204, "", null));
            _transport.Responses.Enqueue(AnalyticsHttpResponse.NetworkError("offline"));

            var result = Run(Create(), 30);

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(25, result.HandledCount);
            Assert.AreEqual(0, result.AttemptedCount);
        }

        [TestCase(408)]
        [TestCase(429)]
        [TestCase(500)]
        [TestCase(503)]
        public void Send_TransientStatus_RetryLater_ChargesThatRequest(long status)
        {
            _transport.Responses.Enqueue(new AnalyticsHttpResponse(status, "", "error"));

            var result = Run(Create(), 30);

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(0, result.HandledCount);
            Assert.AreEqual(25, result.AttemptedCount);
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
        public void Send_TransportThrows_RetryLater_ChargesThatRequest()
        {
            _transport.Throw = true;

            var result = Run(Create(), 1);

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(0, result.HandledCount);
            Assert.AreEqual(1, result.AttemptedCount);
        }

        [Test]
        public void Send_TransportLimit_SplitsRequests()
        {
            var single = Ga4PayloadBuilder.BuildRequests(Envelopes.Many(1), "client_1")[0].ByteCount;
            _transport.MaxBodyBytes = single * 3;

            var result = Run(Create(), 10);

            Assert.IsTrue(result.IsComplete);
            Assert.Greater(_transport.Calls.Count, 1);
            foreach (var call in _transport.Calls)
                Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(call.Body), _transport.MaxBodyBytes);
        }

        [Test]
        public void Send_DeviceAndLocation_Sent()
        {
            var backend = Create();
            backend.Device = new Ga4DeviceInfo { Category = "desktop" };
            backend.UserLocation = new Ga4UserLocation { CountryId = "FR" };

            Run(backend, 1);

            StringAssert.Contains("\"device\":{\"category\":\"desktop\"}", _transport.Calls[0].Body);
            StringAssert.Contains("\"user_location\":{\"country_id\":\"FR\"}", _transport.Calls[0].Body);
        }

        [Test]
        public void Send_ValidateOnly_UsesDebugEndpointAndLogsMessages()
        {
            _settings.ValidateOnly = true;
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

        [Test]
        public void DeviceInfo_FromCurrentPlatform_FillsBasics()
        {
            var device = Ga4DeviceInfo.FromCurrentPlatform();

            Assert.IsNotNull(device);
            Assert.IsNotEmpty(device.OperatingSystem);
        }
    }
}
