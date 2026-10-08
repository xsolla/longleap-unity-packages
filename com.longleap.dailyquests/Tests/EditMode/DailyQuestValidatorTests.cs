using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace LongLeap.DailyQuests.Tests
{
    [TestFixture]
    public class DailyQuestValidatorTests
    {
        private static QuestDefinition Quest(string id, string stat = "grind", int target = 10, int amount = 1, int weight = 1) =>
            new QuestDefinition
            {
                Id = new QuestId(id),
                Weight = weight,
                Objectives = new[] { new QuestObjective { Id = new QuestObjectiveId("o0"), StatPath = stat, StatTarget = target } },
                Rewards = new[] { new QuestReward { Key = new QuestRewardKey("soft"), Amount = amount } }
            };

        private static DailyQuestValidationResult Validate(params QuestDefinition[] quests) =>
            DailyQuestValidator.Validate(new DailyQuestConfig
            {
                Settings = new DailyQuestSettings { DailyCount = 1 },
                Quests = quests
            });

        [Test]
        public void ValidQuest_NoErrors()
        {
            var result = Validate(Quest("a"));

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(DailyQuestValidator.IsPlayable(Quest("a")));
        }

        [Test]
        public void DuplicateIds_IsError()
        {
            Assert.IsFalse(Validate(Quest("a"), Quest("a")).IsValid);
        }

        [TestCase("", 10, 1)]
        [TestCase("bad::path", 10, 1)]
        [TestCase("grind", 0, 1)]
        [TestCase("grind", 10, 0)]
        public void BrokenQuest_IsErrorAndNotPlayable(string stat, int target, int amount)
        {
            var quest = Quest("a", stat, target, amount);

            Assert.IsFalse(Validate(quest).IsValid);
            Assert.IsFalse(DailyQuestValidator.IsPlayable(quest));
        }

        [Test]
        public void NoObjectives_IsError()
        {
            var quest = new QuestDefinition { Id = new QuestId("a"), Objectives = new QuestObjective[0] };

            Assert.IsFalse(DailyQuestValidator.IsPlayable(quest));
        }

        [Test]
        public void ZeroWeight_IsWarning()
        {
            var result = Validate(Quest("a", weight: 0), Quest("b"));

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("weight 0")));
        }

        [Test]
        public void PoolSmallerThanDailyCount_IsWarning()
        {
            var result = DailyQuestValidator.Validate(new DailyQuestConfig
            {
                Settings = new DailyQuestSettings { DailyCount = 3 },
                Quests = new[] { Quest("a") }
            });

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("DailyCount")));
        }

        [Test]
        public void Selector_SameSeed_SameResult()
        {
            var pool = Enumerable.Range(0, 10).Select(i => Quest("q" + i)).ToList();

            var first = DailyQuestSelector.Pick(pool, 3, 123).Select(q => q.Id).ToList();
            var second = DailyQuestSelector.Pick(pool, 3, 123).Select(q => q.Id).ToList();

            CollectionAssert.AreEqual(first, second);
            Assert.AreEqual(3, first.Distinct().Count());
        }

        [Test]
        public void Selector_Exclude_IsNeverPicked()
        {
            var pool = new List<QuestDefinition> { Quest("a"), Quest("b") };

            var picked = DailyQuestSelector.Pick(pool, 2, 1, exclude: new[] { new QuestId("a") });

            CollectionAssert.AreEqual(new[] { "b" }, picked.Select(q => q.Id.Value).ToArray());
        }

        [Test]
        public void Selector_Avoid_UsedOnlyToFillUp()
        {
            var pool = new List<QuestDefinition> { Quest("a"), Quest("b"), Quest("c") };

            var picked = DailyQuestSelector.Pick(pool, 2, 1, avoid: new[] { new QuestId("a"), new QuestId("b") });

            Assert.AreEqual("c", picked[0].Id.Value);
            Assert.AreEqual(2, picked.Count);
        }
    }
}
