using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace LongLeap.Analytics
{
    // The single source of the GA4 Measurement Protocol wire format, shared by
    // every transport.
    //
    // Request body:
    //   { "client_id": "...", "user_id": "...",              (user_id optional)
    //     "user_properties": { "name": { "value": "..." } },  (optional)
    //     "device": {...}, "user_location": {...},            (optional)
    //     "events": [ { "name": "...", "params": {...}, "timestamp_micros": 0 } ] }
    //
    // user_id and user_properties belong to the request, not the event, so
    // consecutive envelopes are grouped by them (compared by value). Each group
    // is split into requests of at most Ga4Rules.MaxEventsPerRequest events and
    // at most maxBodyBytes of UTF-8. Order is kept.
    public static class Ga4PayloadBuilder
    {
        public const string DefaultEndpoint = "https://www.google-analytics.com/mp/collect";

        private const string CollectPath = "/mp/collect";
        private const string DebugCollectPath = "/debug/mp/collect";

        public sealed class Request
        {
            internal Request(int eventCount, string json, int byteCount)
            {
                EventCount = eventCount;
                Json = json;
                ByteCount = byteCount;
            }

            public int EventCount { get; }
            public string Json { get; }
            public int ByteCount { get; }
        }

        // The URL contains the API secret: never log it.
        public static string BuildUrl(Ga4Settings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var endpoint = string.IsNullOrEmpty(settings.EndpointUrl) ? DefaultEndpoint : settings.EndpointUrl;
            if (settings.ValidateOnly && endpoint.Contains(CollectPath) && !endpoint.Contains(DebugCollectPath))
                endpoint = endpoint.Replace(CollectPath, DebugCollectPath);

            return endpoint +
                   "?measurement_id=" + Uri.EscapeDataString(settings.MeasurementId) +
                   "&api_secret=" + Uri.EscapeDataString(settings.ApiSecret);
        }

        // A single event larger than maxBodyBytes still gets its own request;
        // GA4's own 130 kB limit is far above what 25 params of 100 characters
        // can produce.
        public static List<Request> BuildRequests(
            IReadOnlyList<AnalyticsEnvelope> events,
            string clientId,
            Ga4DeviceInfo device = null,
            Ga4UserLocation location = null,
            int maxBodyBytes = Ga4Rules.MaxRequestBytes)
        {
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentException("A client ID is required.", nameof(clientId));
            if (maxBodyBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maxBodyBytes), maxBodyBytes, "Must be positive.");

            var requests = new List<Request>();
            if (events == null || events.Count == 0)
                return requests;

            var deviceJson = device?.ToJson();
            var locationJson = location?.ToJson();

            var start = 0;
            while (start < events.Count)
            {
                var end = start + 1;
                while (end < events.Count &&
                       end - start < Ga4Rules.MaxEventsPerRequest &&
                       SameRequestScope(events[start], events[end]))
                {
                    end++;
                }

                // Too big: halve until it fits or a single event is left.
                while (true)
                {
                    var json = Serialize(events, start, end, clientId, deviceJson, locationJson);
                    var bytes = Encoding.UTF8.GetByteCount(json);
                    if (bytes <= maxBodyBytes || end - start == 1)
                    {
                        requests.Add(new Request(end - start, json, bytes));
                        break;
                    }

                    end = start + (end - start) / 2;
                }

                start = end;
            }

            return requests;
        }

        private static bool SameRequestScope(AnalyticsEnvelope a, AnalyticsEnvelope b) =>
            string.Equals(a.UserId, b.UserId, StringComparison.Ordinal) &&
            SameProperties(a.UserProperties, b.UserProperties);

        private static bool SameProperties(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a.Count != b.Count)
                return false;

            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out var other) || !string.Equals(pair.Value, other, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static string Serialize(
            IReadOnlyList<AnalyticsEnvelope> events,
            int start,
            int end,
            string clientId,
            Dictionary<string, object> deviceJson,
            Dictionary<string, object> locationJson)
        {
            var first = events[start];
            var payload = new Dictionary<string, object> { ["client_id"] = clientId };

            if (first.UserId != null)
                payload["user_id"] = first.UserId;

            if (first.UserProperties.Count > 0)
            {
                var properties = new Dictionary<string, object>(first.UserProperties.Count);
                foreach (var pair in first.UserProperties)
                    properties[pair.Key] = new Dictionary<string, object> { ["value"] = pair.Value };
                payload["user_properties"] = properties;
            }

            if (deviceJson != null && deviceJson.Count > 0)
                payload["device"] = deviceJson;

            if (locationJson != null && locationJson.Count > 0)
                payload["user_location"] = locationJson;

            var list = new List<object>(end - start);
            for (var i = start; i < end; i++)
            {
                var e = events[i];
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = e.Name.Value,
                    ["params"] = e.Params,
                    ["timestamp_micros"] = e.TimestampMicros,
                });
            }

            payload["events"] = list;
            return JsonConvert.SerializeObject(payload, Formatting.None);
        }
    }
}
