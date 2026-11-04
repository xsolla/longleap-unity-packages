using System.Threading.Tasks;

namespace LongLeap.Analytics
{
    // Moves one HTTP POST. Kept separate from the backend so the payload format
    // lives in one place while the way bytes leave the device can change:
    // UnityWebRequest by default, navigator.sendBeacon on WebGL
    // (WebGlBeaconTransport), a fake in tests.
    //
    // Must not throw for network problems: report them as
    // AnalyticsHttpResponse.NetworkError.
    public interface IAnalyticsTransport
    {
        Task<AnalyticsHttpResponse> PostAsync(string url, string body);
    }

    // Optional: a transport that cannot carry large bodies says so here, and
    // Ga4Backend splits requests to stay under the limit.
    public interface IAnalyticsTransportLimits
    {
        int MaxBodyBytes { get; }
    }

    public readonly struct AnalyticsHttpResponse
    {
        public AnalyticsHttpResponse(long statusCode, string body, string error)
        {
            StatusCode = statusCode;
            Body = body;
            Error = error;
        }

        // 0 when the request never got an HTTP answer.
        public long StatusCode { get; }

        // Response body, or null when the transport cannot read it (beacons).
        public string Body { get; }

        public string Error { get; }

        public bool IsNetworkError => StatusCode == 0;

        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

        public static AnalyticsHttpResponse NetworkError(string error) =>
            new AnalyticsHttpResponse(0, null, error);

        public override string ToString() =>
            IsNetworkError ? $"network error: {Error}" : $"HTTP {StatusCode}{(Error != null ? $" ({Error})" : string.Empty)}";
    }
}
