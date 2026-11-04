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
            var config = new FakeGa4Config { ApiSecret = "a+b/c=" };

            var url = Ga4PayloadBuilder.BuildUrl(config);

            Assert.AreEqual(
                "https://www.google-analytics.com/mp/collect?measurement_id=G-TEST123&api_secret=a%2Bb%2Fc%3D",
                url);
        }

        [Test]
        public void BuildUrl_EmptyEndpoint_UsesDefault()
        {
            var url = Ga4PayloadBuilder.BuildUrl(new FakeGa4Config { EndpointUrl = "" });

            StringAssert.StartsWith(Ga4PayloadBuilder.DefaultEndpoint + "?", url);
        }

        [Test]
        public void BuildUrl_ValidateOnly_UsesDebugEndpoint()
        {
            var config = new FakeGa4Config
            {
                EndpointUrl = "https://region1.google-analytics.com/mp/collect",
                ValidateOnly = true,
            };

            var url = Ga4PayloadBuilder.BuildUrl(config);

            StringAssert.StartsWith("https://region1.google-analytics.com/debug/mp/collect?", url);
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
    }
}
