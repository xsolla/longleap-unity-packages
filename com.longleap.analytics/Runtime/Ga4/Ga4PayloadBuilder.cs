using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LongLeap.Analytics
{
    // The single source of the GA4 Measurement Protocol wire format, shared by
    // every transport.
    //
    // Request body:
    //   { "client_id": "...", "user_id": "...",              (user_id optional)
    //     "user_properties": { "name": { "value": "..." } },  (optional)
    //     "events": [ { "name": "...", "params": {...}, "timestamp_micros": 0 } ] }
    //
    // user_id and user_properties belong to the request, not the event, so
    // consecutive envelopes are grouped by them; each group is then split into
    // requests of at most Ga4Rules.MaxEventsPerRequest events. Order is kept.
    public static class Ga4PayloadBuilder
    {
        public const string DefaultEndpoint = "https://www.google-analytics.com/mp/collect";

        private const string CollectPath = "/mp/collect";
        private const string DebugCollectPath = "/debug/mp/collect";

        public sealed class Request
        {
            internal Request(int eventCount, string json)
            {
                EventCount = eventCount;
                Json = json;
            }

            public int EventCount { get; }
            public string Json { get; }
        }

        // The URL contains the API secret: never log it.
        public static string BuildUrl(IGa4Config config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var endpoint = string.IsNullOrEmpty(config.EndpointUrl) ? DefaultEndpoint : config.EndpointUrl;
            if (config.ValidateOnly && endpoint.Contains(CollectPath) && !endpoint.Contains(DebugCollectPath))
                endpoint = endpoint.Replace(CollectPath, DebugCollectPath);

            return endpoint +
                   "?measurement_id=" + Uri.EscapeDataString(config.MeasurementId ?? string.Empty) +
                   "&api_secret=" + Uri.EscapeDataString(config.ApiSecret ?? string.Empty);
        }

        public static List<Request> BuildRequests(IReadOnlyList<AnalyticsEnvelope> events, string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentException("A client ID is required.", nameof(clientId));

            var requests = new List<Request>();
            if (events == null || events.Count == 0)
                return requests;

            var start = 0;
            while (start < events.Count)
            {
                var first = events[start];
                var end = start + 1;
                while (end < events.Count &&
                       end - start < Ga4Rules.MaxEventsPerRequest &&
                       SameRequestScope(first, events[end]))
                {
                    end++;
                }

                requests.Add(new Request(end - start, Serialize(events, start, end, clientId)));
                start = end;
            }

            return requests;
        }

        private static bool SameRequestScope(AnalyticsEnvelope a, AnalyticsEnvelope b) =>
            string.Equals(a.UserId, b.UserId, StringComparison.Ordinal) &&
            ReferenceEquals(a.UserProperties, b.UserProperties);

        private static string Serialize(IReadOnlyList<AnalyticsEnvelope> events, int start, int end, string clientId)
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
