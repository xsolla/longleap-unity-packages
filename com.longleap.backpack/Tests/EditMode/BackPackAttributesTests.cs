using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LongLeap.Backpack.Tests
{
    [TestFixture]
    public class BackPackAttributesTests
    {
        private const string RewardAndMerge =
            "[{\"name\":{\"en\":\"Reward\"},\"values\":[{\"value\":{\"en\":\"cash_10\"},\"external_id\":\"cash_10\"}]," +
            "\"external_id\":\"reward\"}," +
            "{\"name\":{\"en\":\"Merge\"},\"values\":[{\"value\":{\"en\":\"first_climb\"},\"external_id\":\"first_climb\"}]," +
            "\"external_id\":\"merge\"}]";

        private static IReadOnlyList<BackPackAttribute> Parse(string json) =>
            BackPackAttributes.Parse(json == null ? null : JToken.Parse(json));

        [Test]
        public void Parse_BackpackShape()
        {
            var attributes = Parse(RewardAndMerge);

            Assert.AreEqual(2, attributes.Count);
            Assert.AreEqual("reward", attributes[0].ExternalId);
            Assert.AreEqual("Reward", attributes[0].Name);
            Assert.AreEqual("cash_10", attributes[0].Values[0].ExternalId);
            Assert.AreEqual("cash_10", attributes[0].Values[0].Value);
            Assert.AreEqual("first_climb", attributes.Find("merge").Values[0].ExternalId);
        }

        [Test]
        public void ValueIds_AreTheValueExternalIds()
        {
            CollectionAssert.AreEqual(new[] { "cash_10" }, Parse(RewardAndMerge).ValueIds("reward"));
            CollectionAssert.AreEqual(new[] { "first_climb" }, Parse(RewardAndMerge).ValueIds("merge"));
        }

        [Test]
        public void ValueIds_TakeExternalId_NotTheText_AndNameIgnoresCase()
        {
            CollectionAssert.AreEqual(new[] { "shield_1", "cash_100" }, Parse(
                "[{\"external_id\":\"Reward\",\"values\":[" +
                "{\"external_id\":\"shield_1\",\"value\":{\"en\":\"Shield\"}}," +
                "{\"external_id\":\"cash_100\"}]}]").ValueIds("reward"));
        }

        [Test]
        public void Localized_PrefersEnglish_FallsBackToAnyText()
        {
            Assert.AreEqual("Reward", BackPackAttributes.Localized(JToken.Parse("{\"de\":\"Belohnung\",\"en\":\"Reward\"}")));
            Assert.AreEqual("Belohnung", BackPackAttributes.Localized(JToken.Parse("{\"de\":\"Belohnung\"}")));
            Assert.AreEqual("Reward", BackPackAttributes.Localized(JToken.Parse("\"Reward\"")));
        }

        [Test]
        public void MalformedEntries_AreSkipped()
        {
            var attributes = Parse(
                "[null, 1, {\"name\":{\"en\":\"No id\"}}," +
                "{\"external_id\":\"reward\",\"values\":[null, 5, {\"value\":{\"en\":\"no id\"}}, \"cash_10\"]}]");

            Assert.AreEqual(1, attributes.Count);
            CollectionAssert.AreEqual(new[] { "cash_10" }, attributes.ValueIds("reward"));
        }

        [Test]
        public void NothingUsable_IsEmpty()
        {
            CollectionAssert.IsEmpty(Parse(null));
            CollectionAssert.IsEmpty(Parse("{\"reward\":\"cash_10\"}"));
            CollectionAssert.IsEmpty(Parse("[]"));
            CollectionAssert.IsEmpty(Parse(RewardAndMerge).ValueIds("other"));
        }
    }
}
