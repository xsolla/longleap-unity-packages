using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.Analytics
{
    // Sends events to GA4 through the Measurement Protocol.
    //
    //     var backend = new Ga4Backend(settings, AnalyticsClientId.GetOrCreate(prefs))
    //     {
    //         Device = Ga4DeviceInfo.FromCurrentPlatform(),
    //     };
    //
    // Requests go out one after another. A network error or timeout stops the
    // batch with RetryLater and charges no attempt. HTTP 408, 429 and 5xx stop
    // it with RetryLater and charge the events of that request. Any other
    // non-2xx answer means GA4 will never accept the request, so its events
    // are logged and dropped and the batch goes on.
    //
    // GA4 answers 2xx even for malformed events and drops them silently. Turn
    // on Ga4Settings.ValidateOnly during development to get the reasons logged.
    public sealed class Ga4Backend : IAnalyticsBackend
    {
        private readonly Ga4Settings _settings;
        private readonly string _clientId;
        private readonly IAnalyticsTransport _transport;
        private int _maxRequestBytes = Ga4Rules.MaxRequestBytes;

        // transport: null means UnityWebRequestAnalyticsTransport.
        public Ga4Backend(Ga4Settings settings, string clientId, IAnalyticsTransport transport = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentException("A client ID is required; see AnalyticsClientId.", nameof(clientId));

            _clientId = clientId;
            _transport = transport ?? new UnityWebRequestAnalyticsTransport();
        }

        // Sent with every request. Null sends no device data, and GA4 then
        // reports platform, OS and language as "(not set)".
        public Ga4DeviceInfo Device { get; set; }

        // Sent with every request. GA4 does not geolocate Measurement Protocol
        // traffic, so country and city stay "(not set)" without it.
        public Ga4UserLocation UserLocation { get; set; }

        // Upper bound for one request body. The effective limit is the lower
        // of this and the transport's IAnalyticsTransportLimits.MaxBodyBytes.
        public int MaxRequestBytes
        {
            get => _maxRequestBytes;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
                _maxRequestBytes = value;
            }
        }

        public async Task<SendResult> SendAsync(IReadOnlyList<AnalyticsEnvelope> batch)
        {
            if (batch == null || batch.Count == 0)
                return SendResult.Complete;

            var limit = _maxRequestBytes;
            if (_transport is IAnalyticsTransportLimits limits && limits.MaxBodyBytes > 0)
                limit = Math.Min(limit, limits.MaxBodyBytes);

            var url = Ga4PayloadBuilder.BuildUrl(_settings);
            var requests = Ga4PayloadBuilder.BuildRequests(batch, _clientId, Device, UserLocation, limit);
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
                    // A throwing transport is a bug, not a network state:
                    // charge the attempt so a broken setup cannot loop forever.
                    return SendResult.RetryLater(handled, ex.Message, request.EventCount);
                }

                if (response.IsSuccess)
                {
                    if (_settings.ValidateOnly)
                        LogValidation(response, request.EventCount);
                }
                else if (response.IsNetworkError)
                {
                    return SendResult.RetryLater(handled, response.ToString());
                }
                else if (IsTransientStatus(response.StatusCode))
                {
                    return SendResult.RetryLater(handled, response.ToString(), request.EventCount);
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

        private static bool IsTransientStatus(long statusCode) =>
            statusCode == 408 || statusCode == 429 || statusCode >= 500;

        private static void LogValidation(AnalyticsHttpResponse response, int eventCount)
        {
            if (string.IsNullOrEmpty(response.Body))
            {
                Debug.LogWarning(
                    "Ga4Backend: validation mode, but the transport returned no response body to check " +
                    "(beacon transports cannot read responses; use UnityWebRequestAnalyticsTransport).");
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
