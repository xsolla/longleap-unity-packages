using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.Analytics
{
    // Sends events to GA4 through the Measurement Protocol.
    //
    //     var backend = new Ga4Backend(ga4Config, AnalyticsClientId.GetOrCreate(prefs));
    //
    // Requests go out one after another. Network errors, timeouts, HTTP 408,
    // 429 and 5xx stop the batch and report RetryLater for the rest; any other
    // non-2xx answer means GA4 will never accept that request, so its events
    // are logged and dropped and the batch goes on.
    //
    // GA4 answers 2xx even for malformed events and drops them silently. Turn
    // on IGa4Config.ValidateOnly during development to get the reasons logged.
    public sealed class Ga4Backend : IAnalyticsBackend
    {
        private readonly IGa4Config _config;
        private readonly string _clientId;
        private readonly IAnalyticsTransport _transport;

        // transport: null means UnityWebRequestAnalyticsTransport.
        public Ga4Backend(IGa4Config config, string clientId, IAnalyticsTransport transport = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrEmpty(config.MeasurementId))
                throw new ArgumentException("IGa4Config.MeasurementId is empty.", nameof(config));
            if (string.IsNullOrEmpty(config.ApiSecret))
                throw new ArgumentException("IGa4Config.ApiSecret is empty.", nameof(config));
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentException("A client ID is required; see AnalyticsClientId.", nameof(clientId));

            _clientId = clientId;
            _transport = transport ?? new UnityWebRequestAnalyticsTransport();
        }

        public async Task<SendResult> SendAsync(IReadOnlyList<AnalyticsEnvelope> batch)
        {
            if (batch == null || batch.Count == 0)
                return SendResult.Complete;

            var url = Ga4PayloadBuilder.BuildUrl(_config);
            var requests = Ga4PayloadBuilder.BuildRequests(batch, _clientId);
            var handled = 0;

            foreach (var request in requests)
            {
                AnalyticsHttpResponse response;
                try
                {
                    response = await _transport.PostAsync(url, request.Json);
                }
                catch (Exception ex)
                {
                    return SendResult.RetryLater(handled, ex.Message);
                }

                if (response.IsSuccess)
                {
                    if (_config.ValidateOnly)
                        LogValidation(response, request.EventCount);
                }
                else if (IsTransient(response))
                {
                    return SendResult.RetryLater(handled, response.ToString());
                }
                else
                {
                    Debug.LogError(
                        $"Ga4Backend: GA4 rejected a request ({response}); {request.EventCount} event(s) dropped. " +
                        $"Check the measurement ID and API secret. Response: {response.Body}");
                }

                handled += request.EventCount;
            }

            return SendResult.Complete;
        }

        private static bool IsTransient(AnalyticsHttpResponse response) =>
            response.IsNetworkError ||
            response.StatusCode == 408 ||
            response.StatusCode == 429 ||
            response.StatusCode >= 500;

        private static void LogValidation(AnalyticsHttpResponse response, int eventCount)
        {
            if (string.IsNullOrEmpty(response.Body))
            {
                Debug.LogWarning("Ga4Backend: validation mode, but the transport returned no response body to check.");
                return;
            }

            JArray messages;
            try
            {
                messages = JObject.Parse(response.Body)["validationMessages"] as JArray;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Ga4Backend: could not parse the validation response: {ex.Message}");
                return;
            }

            if (messages == null || messages.Count == 0)
            {
                Debug.Log($"Ga4Backend: validation passed for {eventCount} event(s).");
                return;
            }

            foreach (var message in messages)
            {
                Debug.LogWarning(
                    $"Ga4Backend: validation: {message["validationCode"]} at '{message["fieldPath"]}': {message["description"]}");
            }
        }
    }
}
