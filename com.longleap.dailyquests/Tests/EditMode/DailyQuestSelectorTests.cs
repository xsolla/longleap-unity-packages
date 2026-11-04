using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using LongLeap.Core;

namespace LongLeap.DailyQuests.Tests
{
    /// <summary>Selection rules: difficulty slots, hardestLast, unique categories and their relaxation.</summary>
    [TestFixture]
    public class DailyQuestSelectorTests
    {
        private static QuestDefinition Q(string id, int difficulty, params string[] categories) =>
            new QuestDefinition
            {
                Id = new QuestId(id),
                Difficulty = difficulty,
                Categories = categories,
                Objectives = new[] { new QuestObjective { Id = new QuestObjectiveId("o0"), StatPath = "grind", StatTarget = 1 } },
                Rewards = new[] { new QuestReward { Key = new RewardKey("soft"), Amount = 1 } }
            };

        private static DailyQuestSettings Rules(int count, bool unique = false, bool hardestLast = false, params int[] slots) =>
            new DailyQuestSettings
            {
                DailyCount = count,
                UniqueCategories = unique,
                HardestLast = hardestLast,
                SlotDifficulties = slots
            };

        /// <summary>Like the game's daily_quests.json: four categories, each with difficulties 1..3.</summary>
        private static List<QuestDefinition> GameLikePool()
        {
            var pool = new List<QuestDefinition>();
            foreach (var category in new[] { "distance", "height", "grabs", "hands" })
            for (int d = 1; d <= 3; d++)
                pool.Add(Q($"{category}_{d}", d, category));
            return pool;
        }

        private static int[] Difficulties(IEnumerable<QuestDefinition> quests) => quests.Select(q => q.Difficulty).ToArray();

        private static bool CategoriesUnique(IReadOnlyCollection<QuestDefinition> quests) =>
            quests.SelectMany(q => q.Categories).Distinct().Count() == quests.Sum(q => q.Categories.Length);

        [Test]
        public void GameConfig_EverySeed_FullSetBySlotsWithUniqueCategories()
        {
            var pool = GameLikePool();
            var settings = Rules(3, unique: true, hardestLast: true, 1, 2, 3);

            for (int seed = 0; seed < 200; seed++)
            {
                var picked = DailyQuestSelector.Pick(pool, 3, seed, settings: settings);

                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Difficulties(picked), $"seed {seed}");
                Assert.IsTrue(CategoriesUnique(picked), $"seed {seed}");
            }
        }

        [Test]
        public void SameSeed_SameResult_WithRules()
        {
            var pool = GameLikePool();
            var settings = Rules(3, unique: true, hardestLast: true, 1, 2, 3);

            var first = DailyQuestSelector.Pick(pool, 3, 77, settings: settings).Select(q => q.Id).ToList();
            var second = DailyQuestSelector.Pick(pool, 3, 77, settings: settings).Select(q => q.Id).ToList();

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void Slots_ShorterThanDailyCount_RestHasAnyDifficulty()
        {
            var pool = new List<QuestDefinition> { Q("hard", 3), Q("e1", 1), Q("e2", 1), Q("e3", 1), Q("e4", 1) };

            var picked = DailyQuestSelector.Pick(pool, 3, 1, settings: Rules(3, slots: 3));

            Assert.AreEqual(3, picked.Count, "the set used to be cut down to the number of slots");
            Assert.AreEqual("hard", picked.Last().Id.Value);
        }

        [Test]
        public void Slots_LongerThanDailyCount_HardestAreUsed()
        {
            var pool = new List<QuestDefinition> { Q("a", 1), Q("b", 2), Q("c", 3) };

            var picked = DailyQuestSelector.Pick(pool, 2, 1, settings: Rules(2, slots: new[] { 1, 2, 3 }));

            CollectionAssert.AreEqual(new[] { 2, 3 }, Difficulties(picked));
        }

        [Test]
        public void Slots_WithoutFlags_AreHonored()
        {
            var pool = new List<QuestDefinition>();
            for (int d = 1; d <= 3; d++)
            for (int i = 0; i < 3; i++)
                pool.Add(Q($"q{d}_{i}", d));

            for (int seed = 0; seed < 50; seed++)
            {
                var picked = DailyQuestSelector.Pick(pool, 2, seed, settings: Rules(2, slots: new[] { 1, 2 }));
                CollectionAssert.AreEqual(new[] { 1, 2 }, Difficulties(picked), $"seed {seed}");
            }
        }

        [Test]
        public void HardestLast_WithoutSlots_ExactlyOneHardestAndItIsLast()
        {
            var pool = new List<QuestDefinition> { Q("a", 1), Q("b", 1), Q("c", 2), Q("d", 2), Q("e", 3), Q("f", 3) };

            for (int seed = 0; seed < 50; seed++)
            {
                var picked = DailyQuestSelector.Pick(pool, 3, seed, settings: Rules(3, hardestLast: true));

                Assert.AreEqual(3, picked.Count);
                Assert.AreEqual(1, picked.Count(q => q.Difficulty == 3), $"seed {seed}");
                Assert.AreEqual(3, picked.Last().Difficulty, $"seed {seed}");
            }
        }

        [Test]
        public void CategoriesUnsatisfiable_RelaxesCategories_KeepsSlots()
        {
            // There are no three hard quests without a shared category: two of the three are "x".
            var pool = new List<QuestDefinition> { Q("x1", 3, "x"), Q("x2", 3, "x"), Q("y", 3, "y"), Q("easy", 1, "z") };

            var picked = DailyQuestSelector.Pick(pool, 3, 1, settings: Rules(3, unique: true, slots: new[] { 3, 3, 3 }));

            CollectionAssert.AreEqual(new[] { 3, 3, 3 }, Difficulties(picked));
        }

        [Test]
        public void DifficultyUnsatisfiable_KeepsUniqueCategories()
        {
            // There are no difficulty-3 quests at all.
            var pool = new List<QuestDefinition> { Q("a1", 1, "a"), Q("a2", 2, "a"), Q("b1", 1, "b"), Q("c2", 2, "c") };

            var picked = DailyQuestSelector.Pick(pool, 3, 1, settings: Rules(3, unique: true, slots: new[] { 1, 2, 3 }));

            Assert.AreEqual(3, picked.Count, "the set used to stay empty");
            Assert.IsTrue(CategoriesUnique(picked));
        }

        [Test]
        public void NothingSatisfiable_StillFullSet()
        {
            var pool = new List<QuestDefinition> { Q("a", 1, "x"), Q("b", 1, "x"), Q("c", 1, "x") };

            var picked = DailyQuestSelector.Pick(pool, 3, 1, settings: Rules(3, unique: true, hardestLast: true, 1, 2, 3));

            Assert.AreEqual(3, picked.Count);
        }

        [Test]
        public void Selected_TakesItsSlotAndCategory_AndIsNotPickedAgain()
        {
            var a1 = Q("a1", 1, "a");
            var pool = new List<QuestDefinition> { a1, Q("a2", 2, "a"), Q("b2", 2, "b"), Q("a3", 3, "a"), Q("c3", 3, "c"), Q("b1", 1, "b") };

            var picked = DailyQuestSelector.Pick(pool, 2, 1,
                settings: Rules(3, unique: true, hardestLast: true, 1, 2, 3),
                selected: new[] { a1 });

            CollectionAssert.AreEqual(new[] { "b2", "c3" }, picked.Select(q => q.Id.Value).ToArray());
        }

        [Test]
        public void Rules_ZeroWeightAndExcluded_AreNeverPicked()
        {
            var off = new QuestDefinition { Id = new QuestId("off"), Weight = 0, Difficulty = 3, Categories = new[] { "z" } };
            var pool = new List<QuestDefinition> { off, Q("x", 3, "x"), Q("a", 1, "a"), Q("b", 2, "b") };

            var picked = DailyQuestSelector.Pick(pool, 3, 1,
                exclude: new[] { new QuestId("x") },
                settings: Rules(3, unique: true, slots: new[] { 1, 2, 3 }));

            CollectionAssert.DoesNotContain(picked.Select(q => q.Id.Value).ToArray(), "off");
            CollectionAssert.DoesNotContain(picked.Select(q => q.Id.Value).ToArray(), "x");
        }

        [Test]
        public void Rules_AvoidedQuests_UsedOnlyToFillUp()
        {
            var pool = new List<QuestDefinition> { Q("a1", 1, "a"), Q("b1", 1, "b"), Q("c2", 2, "c") };

            var picked = DailyQuestSelector.Pick(pool, 2, 1,
                avoid: new[] { new QuestId("a1") },
                settings: Rules(2, unique: true, slots: new[] { 1, 2 }));

            CollectionAssert.AreEqual(new[] { "b1", "c2" }, picked.Select(q => q.Id.Value).ToArray());
        }

        [Test]
        public void LargeUnsatisfiablePool_FinishesQuickly()
        {
            // Five quests without shared categories cannot be built from four categories, and
            // a full search is 75^4 options. The search budget saves the day.
            var pool = Enumerable.Range(0, 300).Select(i => Q("q" + i, 0, "c" + i % 4)).ToList();

            var watch = Stopwatch.StartNew();
            var picked = DailyQuestSelector.Pick(pool, 5, 1, settings: Rules(5, unique: true));
            watch.Stop();

            Assert.AreEqual(5, picked.Count);
            Assert.Less(watch.ElapsedMilliseconds, 2000);
        }

        [Test]
        public void CanSatisfy_ReportsFeasibility()
        {
            Assert.IsTrue(DailyQuestSelector.CanSatisfy(GameLikePool(), Rules(3, unique: true, hardestLast: true, 1, 2, 3)));
            Assert.IsFalse(DailyQuestSelector.CanSatisfy(GameLikePool(), Rules(5, unique: true)), "there are only four categories");
            Assert.IsTrue(DailyQuestSelector.CanSatisfy(GameLikePool(), Rules(5)), "without rules it is always satisfiable");
        }
    }
}
