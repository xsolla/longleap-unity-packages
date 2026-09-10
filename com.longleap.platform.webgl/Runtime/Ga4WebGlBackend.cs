#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LongLeap.Analytics;
using Newtonsoft.Json;

namespace LongLeap.Platform.WebGL
{
    public sealed class Ga4WebGlBackend : IAnalyticsBackend
    {
        private const int MaxBatchSize = 25;
        private readonly IGa4Config _config;

        [DllImport("__Internal")]
        private static extern void SendGa4Beacon(string url, string body);

        public Ga4WebGlBackend(IGa4Config config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void SendBatch(IReadOnlyList<IAnalyticsEvent> events)
        {
            if (events == null || events.Count == 0) return;

            for (int i = 0; i < events.Count; i += MaxBatchSize)
            {
                var chunk = new List<IAnalyticsEvent>(MaxBatchSize);
                for (int j = 0; j < MaxBatchSize && i + j < events.Count; j++)
                    chunk.Add(events[i + j]);

                var payload = BuildPayload(chunk);
                var json = JsonConvert.SerializeObject(payload);
                var url = $"{_config.EndpointUrl}?measurement_id={_config.MeasurementId}&api_secret={_config.ApiSecret}";
                SendGa4Beacon(url, json);
            }
        }

        private Dictionary<string, object> BuildPayload(List<IAnalyticsEvent> chunk)
        {
            var eventsList = new List<Dictionary<string, object>>(chunk.Count);

            foreach (var e in chunk)
            {
                var eventParams = e.Params is Dictionary<string, object> dict
                    ? dict
                    : new Dictionary<string, object>(e.Params ?? new Dictionary<string, object>());

                if (e.SessionId > 0 && !eventParams.ContainsKey("session_id"))
                    eventParams["session_id"] = e.SessionId;

                var eventDict = new Dictionary<string, object>
                {
                    ["name"] = e.EventName.Value,
                    ["params"] = eventParams,
                    ["timestamp_micros"] = e.TimestampMicros
                };
                eventsList.Add(eventDict);
            }

            return new Dictionary<string, object>
            {
                ["client_id"] = _config.ClientId,
                ["events"] = eventsList
            };
        }
    }
}
#endif
