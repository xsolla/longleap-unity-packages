using System.Collections.Generic;

namespace LongLeap.Analytics
{
    // The request-level "user_location" object of the Measurement Protocol.
    // GA4 does not geolocate Measurement Protocol traffic from the sender's
    // IP, so country and city reports stay "(not set)" unless the game knows
    // the location (for example from its own backend) and sets it here:
    //
    //     backend.UserLocation = new Ga4UserLocation { CountryId = "DE" };
    //
    // Every field is optional; empty ones are not sent.
    public sealed class Ga4UserLocation
    {
        // City name in English: "Berlin".
        public string City { get; set; }

        // ISO 3166-1 alpha-2 country plus ISO 3166-2 subdivision: "US-CA".
        public string RegionId { get; set; }

        // ISO 3166-1 alpha-2: "DE".
        public string CountryId { get; set; }

        // UN M49 code: "155" (Western Europe).
        public string SubcontinentId { get; set; }

        // UN M49 code: "150" (Europe).
        public string ContinentId { get; set; }

        internal Dictionary<string, object> ToJson()
        {
            var json = new Dictionary<string, object>();
            Add(json, "city", City);
            Add(json, "region_id", RegionId);
            Add(json, "country_id", CountryId);
            Add(json, "subcontinent_id", SubcontinentId);
            Add(json, "continent_id", ContinentId);
            return json;
        }

        private static void Add(Dictionary<string, object> json, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                json[key] = value;
        }
    }
}
