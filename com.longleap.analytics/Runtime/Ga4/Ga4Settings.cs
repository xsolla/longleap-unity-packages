using System;

namespace LongLeap.Analytics
{
    // GA4 Measurement Protocol settings for one GA4 data stream. Build it in
    // code, or with Ga4ConfigSO.ToSettings() from an asset.
    //
    // A sealed class rather than an interface, so settings can be added in
    // later versions without breaking anyone's implementation.
    //
    // The client ID is not here: it identifies an installation, not the
    // project. See AnalyticsClientId.
    public sealed class Ga4Settings
    {
        // measurementId: "G-XXXXXXXXXX", from GA4 Admin -> Data streams -> your stream.
        // apiSecret: same stream -> Measurement Protocol API secrets. It ships
        // inside the build and can be extracted: treat it as public, and rotate
        // it if the property receives junk traffic.
        public Ga4Settings(string measurementId, string apiSecret)
        {
            if (string.IsNullOrWhiteSpace(measurementId))
                throw new ArgumentException("The GA4 measurement ID is empty.", nameof(measurementId));
            if (string.IsNullOrWhiteSpace(apiSecret))
                throw new ArgumentException("The GA4 API secret is empty.", nameof(apiSecret));

            MeasurementId = measurementId.Trim();
            ApiSecret = apiSecret.Trim();
        }

        public string MeasurementId { get; }

        public string ApiSecret { get; }

        // Collect endpoint. Null or empty means Ga4PayloadBuilder.DefaultEndpoint.
        // Use https://region1.google-analytics.com/mp/collect to keep data in the EU.
        public string EndpointUrl { get; set; } = Ga4PayloadBuilder.DefaultEndpoint;

        // Sends to the /debug/mp/collect validation endpoint instead: nothing
        // is recorded and Ga4Backend logs GA4's validation messages. For
        // development only; needs a transport that can read responses.
        public bool ValidateOnly { get; set; }
    }
}
