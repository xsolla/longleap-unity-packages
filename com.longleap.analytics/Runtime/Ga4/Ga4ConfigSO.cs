using System;
using UnityEngine;

namespace LongLeap.Analytics
{
    // GA4 settings as an asset: Assets -> Create -> LongLeap Services -> GA4 Config.
    //
    //     var backend = new Ga4Backend(ga4ConfigAsset.ToSettings(), clientId);
    [CreateAssetMenu(menuName = "LongLeap Services/GA4 Config", fileName = "Ga4Config")]
    public sealed class Ga4ConfigSO : ScriptableObject
    {
        [Tooltip("GA4 Admin -> Data streams -> your stream. Looks like G-XXXXXXXXXX.")]
        [SerializeField] private string _measurementId;

        [Tooltip("GA4 Admin -> Data streams -> your stream -> Measurement Protocol API secrets. Ships inside the build: treat as public.")]
        [SerializeField] private string _apiSecret;

        [Tooltip("Collect endpoint. Use https://region1.google-analytics.com/mp/collect to keep data in the EU.")]
        [SerializeField] private string _endpointUrl = Ga4PayloadBuilder.DefaultEndpoint;

        [Tooltip("Send to the validation endpoint: nothing is recorded, GA4's validation messages are logged. Development only.")]
        [SerializeField] private bool _validateOnly;

        public string MeasurementId => _measurementId;
        public string ApiSecret => _apiSecret;
        public string EndpointUrl => _endpointUrl;
        public bool ValidateOnly => _validateOnly;

        // True when both the measurement ID and the API secret are filled in.
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_measurementId) && !string.IsNullOrWhiteSpace(_apiSecret);

        // Throws InvalidOperationException when the asset is not filled in;
        // check IsConfigured first to fall back to another backend instead.
        public Ga4Settings ToSettings()
        {
            if (!IsConfigured)
                throw new InvalidOperationException($"Ga4ConfigSO '{name}': fill in the measurement ID and the API secret.");

            return new Ga4Settings(_measurementId, _apiSecret)
            {
                EndpointUrl = _endpointUrl,
                ValidateOnly = _validateOnly,
            };
        }
    }
}
