using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LongLeap.Analytics.Tests
{
    public class Ga4PayloadBuilderTests
    {
        [Test]
        public void BuildUrl_AddsEscapedCredentials()
        {
            var url = Ga4PayloadBuilder.BuildUrl(TestSettings.Create("a+b/c="));

            Assert.AreEqual(
                "https://www.google-analytics.com/mp/collect?measurement_id=G-TEST123&api_secret=a%2Bb%2Fc%3D",
                url);
        }

        [Test]
        public void BuildUrl_EmptyEndpoint_UsesDefault()
        {
            var settings = TestSettings.Create();
            settings.EndpointUrl = "";

            StringAssert.StartsWith(Ga4PayloadBuilder.DefaultEndpoint + "?", Ga4PayloadBuilder.BuildUrl(settings));
        }

        [Test]
        public void BuildUrl_ValidateOnly_UsesDebugEndpoint()
        {
            var settings = TestSettings.Create();
            settings.EndpointUrl = "https://region1.google-analytics.com/mp/collect";
            settings.ValidateOnly = true;

            StringAssert.StartsWith("https://region1.google-analytics.com/debug/mp/collect?", Ga4PayloadBuilder.BuildUrl(settings));
        }

        [Test]
        public void BuildRequests_NoClientId_Throws()
        {
            Assert.Throws<ArgumentException>(() => Ga4PayloadBuilder.BuildRequests(Envelopes.Many(1), ""));
        }

        [Test]
        public void BuildRequests_Empty_ReturnsNoRequests()
        {
            Assert.IsEmpty(Ga4PayloadBuilder.BuildRequests(new List<AnalyticsEnvelope>(), "client"));
        }

        [Test]
        public void BuildRequests_SingleEvent_MatchesWireFormat()
        {
            var envelope = Envelopes.Make("level_up", 1234567L, parameters: new (string, object)[]
            {
                ("hero_id", "ronin"),
                ("level", 3),
                ("ratio", 0.5),
            });

            var requests = Ga4PayloadBuilder.BuildRequests(new[] { envelope }, "client_1");

            Assert.AreEqual(1, requests.Count);
            Assert.AreEqual(1, requests[0].EventCount);

            var json = JObject.Parse(requests[0].Json);
            Assert.AreEqual("client_1", (string)json["client_id"]);
            Assert.IsNull(json["user_id"]);
            Assert.IsNull(json["user_properties"]);
            Assert.IsNull(json["device"]);
            Assert.IsNull(json["user_location"]);

            var e = json["events"][0];
            Assert.AreEqual("level_up", (string)e["name"]);
            Assert.AreEqual(1234567L, (long)e["timestamp_micros"]);
            Assert.AreEqual("ronin", (string)e["params"]["hero_id"]);
            Assert.AreEqual(3L, (long)e["params"]["level"]);
            Assert.AreEqual(0.5d, (double)e["params"]["ratio"]);
        }

        [Test]
        public void BuildRequests_UserIdAndProperties_AtRequestLevel()
        {
            var properties = new Dictionary<string, string> { ["tier"] = "gold" };
            var envelope = Envelopes.Make(userId: "player_42", userProperties: properties);

            var json = JObject.Parse(Ga4PayloadBuilder.BuildRequests(new[] { envelope }, "client")[0].Json);

            Assert.AreEqual("player_42", (string)json["user_id"]);
            Assert.AreEqual("gold", (string)json["user_properties"]["tier"]["value"]);
        }

        [Test]
        public void BuildRequests_DeviceAndLocation_OnlyNonEmptyFields()
        {
            var device = new Ga4DeviceInfo { Category = "mobile", Language = "en", OperatingSystem = "Android", Model = "" };
            var location = new Ga4UserLocation { CountryId = "DE" };

            var json = JObject.Parse(Ga4PayloadBuilder.BuildRequests(Envelopes.Many(1), "client", device, location)[0].Json);

            Assert.AreEqual("mobile", (string)json["device"]["category"]);
            Assert.AreEqual("en", (string)json["device"]["language"]);
            Assert.AreEqual("Android", (string)json["device"]["operating_system"]);
            Assert.IsNull(json["device"]["model"]);
            Assert.AreEqual("DE", (string)json["user_location"]["country_id"]);
            Assert.IsNull(json["user_location"]["city"]);
        }

        [Test]
        public void BuildRequests_EmptyDevice_NotSent()
        {
            var json = JObject.Parse(Ga4PayloadBuilder.BuildRequests(Envelopes.Many(1), "client", new Ga4DeviceInfo())[0].Json);

            Assert.IsNull(json["device"]);
        }

        [Test]
        public void BuildRequests_30Events_SplitInto25And5()
        {
            var requests = Ga4PayloadBuilder.BuildRequests(Envelopes.Many(30), "client");

            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual(25, requests[0].EventCount);
            Assert.AreEqual(5, requests[1].EventCount);
            Assert.AreEqual("event_25", (string)JObject.Parse(requests[1].Json)["events"][0]["name"]);
        }

        [Test]
        public void BuildRequests_UserChange_StartsNewRequest()
        {
            var events = new List<AnalyticsEnvelope>();
            events.AddRange(Envelopes.Many(2));
            events.AddRange(Envelopes.Many(3, userId: "player_42"));

            var requests = Ga4PayloadBuilder.BuildRequests(events, "client");

            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual(2, requests[0].EventCount);
            Assert.AreEqual(3, requests[1].EventCount);
            Assert.AreEqual("player_42", (string)JObject.Parse(requests[1].Json)["user_id"]);
        }

        [Test]
        public void BuildRequests_EqualUserPropertiesInDifferentInstances_ShareRequest()
        {
            var events = new[]
            {
                Envelopes.Make("event_a", userProperties: new Dictionary<string, string> { ["tier"] = "gold" }),
                Envelopes.Make("event_b", userProperties: new Dictionary<string, string> { ["tier"] = "gold" }),
                Envelopes.Make("event_c", userProperties: new Dictionary<string, string> { ["tier"] = "silver" }),
            };

            var requests = Ga4PayloadBuilder.BuildRequests(events, "client");

            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual(2, requests[0].EventCount);
        }

        [Test]
        public void BuildRequests_OverByteLimit_SplitsUntilEachFits()
        {
            var events = new List<AnalyticsEnvelope>();
            for (var i = 0; i < 20; i++)
                events.Add(Envelopes.Make($"event_{i}", parameters: new (string, object)[] { ("text", new string('x', 100)) }));

            var single = Ga4PayloadBuilder.BuildRequests(events.GetRange(0, 1), "client")[0].ByteCount;
            var limit = single * 6;

            var requests = Ga4PayloadBuilder.BuildRequests(events, "client", maxBodyBytes: limit);

            var total = 0;
            foreach (var request in requests)
            {
                Assert.LessOrEqual(request.ByteCount, limit);
                total += request.EventCount;
            }

            Assert.Greater(requests.Count, 1);
            Assert.AreEqual(20, total);
        }

        [Test]
        public void BuildRequests_SingleEventOverLimit_StillSentAlone()
        {
            var requests = Ga4PayloadBuilder.BuildRequests(Envelopes.Many(2), "client", maxBodyBytes: 10);

            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual(1, requests[0].EventCount);
        }
    }
}
