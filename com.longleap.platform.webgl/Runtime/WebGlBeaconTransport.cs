using System.Threading.Tasks;
using LongLeap.Analytics;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace LongLeap.Platform.WebGL
{
    // Sends analytics requests with navigator.sendBeacon, falling back to
    // fetch with keepalive. Unlike UnityWebRequest, the browser delivers these
    // even when the tab is closing, which is when the last events are flushed.
    //
    //     var backend = new Ga4Backend(config, clientId, WebGlBeaconTransport.CreateForCurrentPlatform());
    //
    // A beacon is fire-and-forget: the page never sees the HTTP answer. A
    // request the browser accepted counts as delivered (204) and is never
    // retried; one it refused is reported as a network error and retried.
    // For the same reason IGa4Config.ValidateOnly needs the UnityWebRequest
    // transport.
    //
    // Compiles everywhere; outside WebGL players it reports a network error.
    public sealed class WebGlBeaconTransport : IAnalyticsTransport
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool IsSupported => true;

        [DllImport("__Internal")]
        private static extern int LongLeapAnalyticsSendBeacon(string url, string body);
#else
        public static bool IsSupported => false;
#endif

        // The beacon transport in WebGL players, UnityWebRequest elsewhere
        // (including the Editor).
        public static IAnalyticsTransport CreateForCurrentPlatform() =>
            IsSupported ? new WebGlBeaconTransport() : new UnityWebRequestAnalyticsTransport();

        public Task<AnalyticsHttpResponse> PostAsync(string url, string body)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var queued = LongLeapAnalyticsSendBeacon(url, body ?? string.Empty) != 0;
            return Task.FromResult(queued
                ? new AnalyticsHttpResponse(204, null, null)
                : AnalyticsHttpResponse.NetworkError("The browser refused to queue the request."));
#else
            return Task.FromResult(
                AnalyticsHttpResponse.NetworkError("WebGlBeaconTransport works only in WebGL player builds."));
#endif
        }
    }
}
