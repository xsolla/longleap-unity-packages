using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Analytics
{
    // A snapshot of one event, taken by AnalyticsService at Send time, plus
    // everything the service stamps on it: timestamp, session, user.
    //
    // Backends only ever see envelopes. The game's event object is copied, not
    // referenced, so it can be reused or changed after Send without touching
    // what is already queued.
    //
    // Parameters are validated and normalized (string/long/double) when the
    // envelope is built; invalid ones are dropped with a warning. Enrichers
    // add params through TrySetParam, which never overwrites a param of the
    // event itself.
    public sealed class AnalyticsEnvelope
    {
        private static readonly IReadOnlyDictionary<string, string> NoUserProperties =
            new Dictionary<string, string>();

        private readonly Dictionary<string, object> _params;

        public AnalyticsEnvelope(
            EventName name,
            IReadOnlyDictionary<string, object> parameters,
            long timestampMicros,
            long sessionId,
            string userId = null,
            IReadOnlyDictionary<string, string> userProperties = null)
        {
            Name = name;
            TimestampMicros = timestampMicros;
            SessionId = sessionId;
            UserId = string.IsNullOrEmpty(userId) ? null : userId;
            UserProperties = userProperties ?? NoUserProperties;
            _params = CopyParams(name, parameters);
        }

        public EventName Name { get; }

        public IReadOnlyDictionary<string, object> Params => _params;

        // Unix time in microseconds, taken when the event was sent.
        public long TimestampMicros { get; }

        public long SessionId { get; }

        // Null when no user ID is set.
        public string UserId { get; }

        // The user properties at Send time. The service hands out a new
        // dictionary whenever they change, so envelopes with the same
        // properties share one instance.
        public IReadOnlyDictionary<string, string> UserProperties { get; }

        // Number of failed delivery attempts so far. Owned by AnalyticsService.
        internal int Attempts { get; set; }

        // Adds a param unless the key is invalid, already present, or the
        // event already holds Ga4Rules.MaxParamsPerEvent params.
        public bool TrySetParam(string key, object value)
        {
            if (!Ga4Rules.IsValidParamName(key, out _) || Ga4Rules.IsServiceParam(key))
                return false;

            return TryAdd(key, value);
        }

        internal void SetServiceParam(string key, object value)
        {
            // Service params live in the reserved slots, so they always fit.
            _params[key] = value;
        }

        private bool TryAdd(string key, object value)
        {
            if (_params.ContainsKey(key) || _params.Count >= Ga4Rules.MaxParamsPerEvent)
                return false;

            if (!Ga4Rules.TryNormalizeParamValue(value, out var normalized, out _))
                return false;

            _params[key] = normalized;
            return true;
        }

        private static Dictionary<string, object> CopyParams(EventName name, IReadOnlyDictionary<string, object> source)
        {
            var result = new Dictionary<string, object>(source?.Count ?? 0, StringComparer.Ordinal);
            if (source == null)
                return result;

            foreach (var pair in source)
            {
                if (!Ga4Rules.IsValidParamName(pair.Key, out var reason))
                {
                    Debug.LogWarning($"AnalyticsEnvelope '{name}': parameter '{pair.Key}' dropped: {reason}.");
                    continue;
                }

                if (Ga4Rules.IsServiceParam(pair.Key))
                {
                    Debug.LogWarning($"AnalyticsEnvelope '{name}': parameter '{pair.Key}' dropped: it is set by AnalyticsService.");
                    continue;
                }

                if (!Ga4Rules.TryNormalizeParamValue(pair.Value, out var normalized, out reason))
                {
                    Debug.LogWarning($"AnalyticsEnvelope '{name}': parameter '{pair.Key}' dropped: {reason}.");
                    continue;
                }

                if (result.Count >= Ga4Rules.MaxCustomParams)
                {
                    Debug.LogWarning(
                        $"AnalyticsEnvelope '{name}': parameter '{pair.Key}' dropped: more than {Ga4Rules.MaxCustomParams} parameters.");
                    continue;
                }

                result[pair.Key] = normalized;
            }

            return result;
        }
    }
}
