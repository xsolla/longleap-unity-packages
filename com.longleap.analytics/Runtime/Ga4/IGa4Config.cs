namespace LongLeap.Analytics
{
    // GA4 Measurement Protocol credentials. Ga4ConfigSO is the ready-made
    // asset; any other source (remote config, build settings) works too.
    //
    // The client ID is not here: it identifies an installation, not the
    // project. See AnalyticsClientId.
    public interface IGa4Config
    {
        // "G-XXXXXXXXXX", from GA4 Admin -> Data streams -> your stream.
        string MeasurementId { get; }

        // GA4 Admin -> Data streams -> your stream -> Measurement Protocol API secrets.
        // It ships inside the build and can be extracted: treat it as public,
        // and rotate it if the property receives junk traffic.
        string ApiSecret { get; }

        // Collect endpoint. Null or empty means Ga4PayloadBuilder.DefaultEndpoint.
        // Use https://region1.google-analytics.com/mp/collect to keep data in the EU.
        string EndpointUrl { get; }

        // Sends to the /debug/mp/collect validation endpoint instead: nothing
        // is recorded and Ga4Backend logs GA4's validation messages. For
        // development only; needs a transport that can read responses.
        bool ValidateOnly { get; }
    }
}
