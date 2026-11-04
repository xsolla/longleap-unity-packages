using System.Threading.Tasks;
using LongLeap.Analytics;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace LongLeap.Platform.WebGL
{
    // Sends analytics requests with navigator.sendBeacon. Unlike
    // UnityWebRequest, the browser delivers a beacon even when the tab is
    // closing, which is when the last events are flushed.
    //
    //     var backend = new Ga4Backend(settings, clientId, WebGlBeaconTransport.CreateForCurrentPlatform(settings));
    //
    // What the browser reports is all there is:
    // - accepted: the request is queued and counts as delivered (HTTP 204).
    //   The page never sees GA4's answer, so it is never retried.
    // - refused: reported as a network error, logged by the service and
    //   retried with backoff. Browsers refuse beacons once the beacons still
    //   in flight exceed about 64 KB, so a large flush at tab close can be
    //   refused partly; whatever is refused then is lost with the page.
    //
    // Each request is kept under MaxBodyBytes (Ga4Backend splits them), so a
    // single request always fits the browser's quota on its own.
    //
    // GA4 validation mode needs to read responses: CreateForCurrentPlatform
    // picks UnityWebRequest when Ga4Settings.ValidateOnly is set.
    //
    // Compiles everywhere; outside WebGL players it reports a network error.
    public sealed class WebGlBeaconTransport : IAnalyticsTransport, IAnalyticsTransportLimits
    {
        // Below the browsers' 64 KiB beacon quota.
        public const int DefaultMaxBodyBytes = 60_000;

#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool IsSupported => true;

        [DllImport("__Internal")]
        private static extern int LongLeapAnalyticsSendBeacon(string url, string body);
#else
        public static bool IsSupported => false;
#endif

        public int MaxBodyBytes => DefaultMaxBodyBytes;

        // The beacon transport in WebGL players, UnityWebRequest elsewhere
        // (including the Editor) and whenever settings.ValidateOnly is set.
        public static IAnalyticsTransport CreateForCurrentPlatform(Ga4Settings settings = null)
        {
            if (!IsSupported || (settings != null && settings.ValidateOnly))
                return new UnityWebRequestAnalyticsTransport();

            return new WebGlBeaconTransport();
        }

        public Task<AnalyticsHttpResponse> PostAsync(string url, string body)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var queued = LongLeapAnalyticsSendBeacon(url, body ?? string.Empty) != 0;
            return Task.FromResult(queued
                ? new AnalyticsHttpResponse(204, null, null)
                : AnalyticsHttpResponse.NetworkError("The browser refused the beacon (quota full or beacons unavailable)."));
#else
            return Task.FromResult(
                AnalyticsHttpResponse.NetworkError("WebGlBeaconTransport works only in WebGL player builds."));
#endif
        }
    }
}
