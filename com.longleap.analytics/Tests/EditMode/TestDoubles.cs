using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;

namespace LongLeap.Analytics.Tests
{
    internal sealed class ManualClock
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan delta) => Now += delta;

        public void AdvanceSeconds(double seconds) => Now += TimeSpan.FromSeconds(seconds);

        public long NowMicros => Now.ToUnixTimeMilliseconds() * 1000L;
    }

    internal sealed class SpyBackend : IAnalyticsBackend
    {
        public readonly List<List<AnalyticsEnvelope>> Batches = new List<List<AnalyticsEnvelope>>();
        public readonly Queue<SendResult> Results = new Queue<SendResult>();

        // When set, every call fails with this result (Results is ignored).
        public SendResult? AlwaysResult;

        // When set, calls return this task instead: a delivery left in flight.
        public TaskCompletionSource<SendResult> Pending;

        public bool Throw;

        // Runs inside SendAsync, before the result is returned.
        public Action<IReadOnlyList<AnalyticsEnvelope>> OnSend;

        public int CallCount => Batches.Count;

        public List<AnalyticsEnvelope> LastBatch => Batches[Batches.Count - 1];

        public Task<SendResult> SendAsync(IReadOnlyList<AnalyticsEnvelope> batch)
        {
            Batches.Add(new List<AnalyticsEnvelope>(batch));
            OnSend?.Invoke(batch);

            if (Throw)
                throw new InvalidOperationException("Simulated backend failure");

            if (Pending != null)
                return Pending.Task;

            if (AlwaysResult.HasValue)
                return Task.FromResult(AlwaysResult.Value);

            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : SendResult.Complete);
        }
    }

    internal sealed class FakeTransport : IAnalyticsTransport, IAnalyticsTransportLimits
    {
        public int MaxBodyBytes { get; set; } = int.MaxValue;

        public readonly List<(string Url, string Body)> Calls = new List<(string, string)>();
        public readonly Queue<AnalyticsHttpResponse> Responses = new Queue<AnalyticsHttpResponse>();
        public bool Throw;

        public Task<AnalyticsHttpResponse> PostAsync(string url, string body)
        {
            Calls.Add((url, body));

            if (Throw)
                throw new InvalidOperationException("Simulated transport failure");

            return Task.FromResult(Responses.Count > 0
                ? Responses.Dequeue()
                : new AnalyticsHttpResponse(204, string.Empty, null));
        }
    }

    internal static class TestSettings
    {
        public static Ga4Settings Create(string apiSecret = "test_secret") =>
            new Ga4Settings("G-TEST123", apiSecret);
    }

    internal sealed class FakeExperimentProvider : IExperimentInfoProvider
    {
        public IReadOnlyList<AbExperimentInfo> ActiveExperiments { get; set; } = Array.Empty<AbExperimentInfo>();
    }

    internal sealed class FakePlayerPrefs : IPlayerPrefs
    {
        public readonly Dictionary<string, string> Strings = new Dictionary<string, string>();
        public int SaveCount;

        public string GetString(string key, string defaultValue = "") =>
            Strings.TryGetValue(key, out var value) ? value : defaultValue;

        public int GetInt(string key, int defaultValue = 0) => defaultValue;
        public float GetFloat(string key, float defaultValue = 0f) => defaultValue;
        public void SetString(string key, string value) => Strings[key] = value;
        public void SetInt(string key, int value) { }
        public void SetFloat(string key, float value) { }
        public bool HasKey(string key) => Strings.ContainsKey(key);
        public void DeleteKey(string key) => Strings.Remove(key);
        public void Save() => SaveCount++;
    }

    internal static class Envelopes
    {
        public static AnalyticsEnvelope Make(
            string name = "test_event",
            long timestampMicros = 1_000_000L,
            string userId = null,
            IReadOnlyDictionary<string, string> userProperties = null,
            long sessionId = 42L,
            params (string Key, object Value)[] parameters)
        {
            var dict = new Dictionary<string, object>();
            foreach (var (key, value) in parameters)
                dict[key] = value;

            return new AnalyticsEnvelope(new EventName(name), dict, timestampMicros, sessionId, userId, userProperties);
        }

        public static List<AnalyticsEnvelope> Many(int count, string userId = null)
        {
            var list = new List<AnalyticsEnvelope>(count);
            for (var i = 0; i < count; i++)
                list.Add(Make($"event_{i}", userId: userId));
            return list;
        }
    }
}
