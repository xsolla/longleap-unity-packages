using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.DailyQuests.Tests
{
    [TestFixture]
    public class DailyQuestJsonTests
    {
        [Test]
        public void Parse_FullQuest_MapsEveryField()
        {
            var config = DailyQuestJson.Parse(@"{
                ""settings"": { ""dailyCount"": 4, ""resetHourUtc"": 6 },
                ""quests"": [{
                    ""id"": ""grind_50"", ""weight"": 3,
                    ""objectives"": [{ ""id"": ""g"", ""statPath"": ""grind"", ""statTarget"": 50, ""mode"": ""absolute"" }],
                    ""rewards"": [{ ""key"": ""soft"", ""amount"": 5 }]
                }]
            }");

            Assert.AreEqual(4, config.Settings.DailyCount);
            Assert.AreEqual(6, config.Settings.ResetHourUtc);

            var quest = config.Quests.Single();
            Assert.AreEqual("grind_50", quest.Id.Value);
            Assert.AreEqual(3, quest.Weight);

            var obj = quest.Objectives.Single();
            Assert.AreEqual("g", obj.Id.Value);
            Assert.AreEqual("grind", obj.StatPath);
            Assert.AreEqual(50, obj.StatTarget);
            Assert.AreEqual(QuestProgressMode.Absolute, obj.Mode);

            var reward = quest.Rewards.Single();
            Assert.AreEqual("soft", reward.Key.Value);
            Assert.AreEqual(5, reward.Amount);
        }

        [Test]
        public void Parse_SelectionRules_MapsSettingsAndQuestFields()
        {
            var config = DailyQuestJson.Parse(@"{
                ""settings"": { ""uniqueCategories"": true, ""hardestLast"": true, ""slotDifficulties"": [1, 2, 3] },
                ""quests"": [{
                    ""id"": ""q"", ""categories"": [""distance"", "" "", "" height ""], ""difficulty"": 2,
                    ""objectives"": [{ ""statPath"": ""grind"", ""statTarget"": 1 }]
                }]
            }");

            Assert.IsTrue(config.Settings.UniqueCategories);
            Assert.IsTrue(config.Settings.HardestLast);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, config.Settings.SlotDifficulties);
            Assert.IsTrue(config.Settings.HasSelectionRules);

            var quest = config.Quests.Single();
            CollectionAssert.AreEqual(new[] { "distance", "height" }, quest.Categories, "empty ones are dropped, whitespace is trimmed");
            Assert.AreEqual(2, quest.Difficulty);
        }

        [Test]
        public void Parse_OptionalFieldsMissing_UsesDefaults()
        {
            var config = DailyQuestJson.Parse(@"{
                ""quests"": [{
                    ""id"": ""q"",
                    ""objectives"": [{ ""statPath"": ""grind"", ""statTarget"": 1 }, { ""statPath"": ""ollie"", ""statTarget"": 1 }]
                }]
            }");

            Assert.AreEqual(DailyQuestSettings.DefaultDailyCount, config.Settings.DailyCount);
            Assert.AreEqual(0, config.Settings.ResetHourUtc);
            Assert.IsFalse(config.Settings.HasSelectionRules);

            var quest = config.Quests.Single();
            Assert.AreEqual(1, quest.Weight);
            CollectionAssert.AreEqual(new[] { "o0", "o1" }, quest.Objectives.Select(o => o.Id.Value).ToArray());
            Assert.AreEqual(QuestProgressMode.Delta, quest.Objectives[0].Mode);
            Assert.IsEmpty(quest.Rewards);
            Assert.IsEmpty(quest.Categories);
            Assert.AreEqual(0, quest.Difficulty);
        }

        [Test]
        public void Parse_Empty_ReturnsEmptyConfig()
        {
            Assert.IsEmpty(DailyQuestJson.Parse("").Quests);
            Assert.IsEmpty(DailyQuestJson.Parse("{}").Quests);
        }

        [Test]
        public async Task JsonProvider_BrokenJson_ReturnsEmptyConfigAndLogs()
        {
            LogAssert.Expect(LogType.Error, new Regex("failed to parse JSON"));

            var config = await new JsonDailyQuestProvider("{ not json").LoadAsync();

            Assert.IsEmpty(config.Quests);
        }

        [Test]
        public void StateStore_Parse_RoundTrip()
        {
            var state = new DailyQuestsState
            {
                PeriodStartUtcTicks = 42,
                Active = new[]
                {
                    new ActiveQuestState
                    {
                        QuestId = "q", IsCompleted = true, IsClaimed = false,
                        Baselines = new[] { new ObjectiveBaseline { ObjectiveId = "o0", Value = 7, IsCompleted = true } }
                    }
                },
                Withheld = true
            };

            var result = JsonDailyQuestStateStore.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(state));

            Assert.AreEqual(DailyQuestStateLoadStatus.Loaded, result.Status);
            var parsed = result.State;
            Assert.AreEqual(42, parsed.PeriodStartUtcTicks);
            Assert.IsTrue(parsed.Withheld);
            Assert.AreEqual("q", parsed.Active[0].QuestId);
            Assert.IsTrue(parsed.Active[0].IsCompleted);
            Assert.AreEqual(7, parsed.Active[0].Baselines[0].Value);
            Assert.IsTrue(parsed.Active[0].Baselines[0].IsCompleted);
        }

        [Test]
        public void StateStore_Parse_Empty_IsFreshStart()
        {
            var parsed = JsonDailyQuestStateStore.Parse("");

            Assert.AreEqual(DailyQuestStateLoadStatus.Empty, parsed.Status);
            Assert.IsNull(parsed.State);
        }

        [Test]
        public void StateStore_Parse_Broken_IsUnreadable()
        {
            var parsed = JsonDailyQuestStateStore.Parse("{ broken");

            Assert.AreEqual(DailyQuestStateLoadStatus.Unreadable, parsed.Status);
        }

        [Test]
        public void StateStore_Parse_NewerVersion_IsUnreadable()
        {
            var parsed = JsonDailyQuestStateStore.Parse(@"{ ""Version"": 999, ""PeriodStartUtcTicks"": 5 }");

            Assert.AreEqual(DailyQuestStateLoadStatus.Unreadable, parsed.Status);
            Assert.IsNull(parsed.State);
        }

        [Test]
        public void StateLoadResult_Default_IsUnavailable()
        {
            Assert.AreEqual(DailyQuestStateLoadStatus.Unavailable, default(DailyQuestStateLoadResult).Status,
                "an unfilled result must not turn quests on");
            Assert.AreEqual(DailyQuestStateLoadStatus.Empty, DailyQuestStateLoadResult.Loaded(null).Status);
        }
    }
}
