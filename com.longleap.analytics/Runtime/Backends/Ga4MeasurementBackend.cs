using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.Analytics
{
public sealed class Ga4MeasurementBackend : IAnalyticsBackend
{
    private const int MaxBatchSize = 25;
    private readonly IGa4Config _config;

    public Ga4MeasurementBackend(IGa4Config config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public void SendBatch(IReadOnlyList<IAnalyticsEvent> events)
    {
        if (events == null || events.Count == 0)
        {
            Debug.LogError("Ga4MeasurementBackend: получен пустой батч.");
            return;
        }

        for (int i = 0; i < events.Count; i += MaxBatchSize)
        {
            var chunk = new List<IAnalyticsEvent>(MaxBatchSize);
            for (int j = 0; j < MaxBatchSize && i + j < events.Count; j++)
                chunk.Add(events[i + j]);

            SendChunkAsync(chunk);
        }
    }

    private async void SendChunkAsync(List<IAnalyticsEvent> chunk)
    {
        try
        {
            var payload = BuildPayload(chunk);
            var json = JsonConvert.SerializeObject(payload);
            var url = BuildUrl();

            using (var request = new UnityWebRequest(url, "POST"))
            {
                var body = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                    await System.Threading.Tasks.Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning($"Ga4MeasurementBackend: ошибка отправки: {request.error}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Ga4MeasurementBackend: исключение при отправке: {ex.Message}");
        }
    }

    public Dictionary<string, object> BuildPayload(List<IAnalyticsEvent> chunk)
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

    public string BuildUrl()
    {
        return $"{_config.EndpointUrl}?measurement_id={_config.MeasurementId}&api_secret={_config.ApiSecret}";
    }
}
}
