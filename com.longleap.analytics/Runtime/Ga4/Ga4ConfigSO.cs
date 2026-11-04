using UnityEngine;

namespace LongLeap.Analytics
{
    // IGa4Config as an asset: Assets -> Create -> LongLeap Services -> GA4 Config.
    [CreateAssetMenu(menuName = "LongLeap Services/GA4 Config", fileName = "Ga4Config")]
    public sealed class Ga4ConfigSO : ScriptableObject, IGa4Config
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
    }
}
