using System.Collections.Generic;
using NUnit.Framework;
using static LongLeap.XsollaId.Accounts.Tests.TestSaves;

namespace LongLeap.XsollaId.Accounts.Tests
{
    [TestFixture]
    public class SaveMergeRulesTests
    {
        [TestCase(SaveMergeRule.Max, "7", "3", ExpectedResult = "7")]
        [TestCase(SaveMergeRule.Max, "7", "9", ExpectedResult = "9")]
        [TestCase(SaveMergeRule.Max, "1.5", "1.25", ExpectedResult = "1.5")]
        // A missing or unparsable side loses.
        [TestCase(SaveMergeRule.Max, null, "4", ExpectedResult = "4")]
        [TestCase(SaveMergeRule.Max, "4", null, ExpectedResult = "4")]
        [TestCase(SaveMergeRule.Max, "junk", "4", ExpectedResult = "4")]
        // Neither parses: keep the account's raw value.
        [TestCase(SaveMergeRule.Max, "other", "junk", ExpectedResult = "junk")]
        // 2^53 + 1 and 2^53 are the same double: integers compare exactly.
        [TestCase(SaveMergeRule.Max, "9007199254740993", "9007199254740992", ExpectedResult = "9007199254740993")]
        [TestCase(SaveMergeRule.Min, "9007199254740993", "9007199254740992", ExpectedResult = "9007199254740992")]
        // Min ignores zero as "unknown".
        [TestCase(SaveMergeRule.Min, "100", "200", ExpectedResult = "100")]
        [TestCase(SaveMergeRule.Min, "0", "200", ExpectedResult = "200")]
        [TestCase(SaveMergeRule.Min, null, "200", ExpectedResult = "200")]
        // Or: set if either is set.
        [TestCase(SaveMergeRule.Or, "1", null, ExpectedResult = "1")]
        [TestCase(SaveMergeRule.Or, "0", "1", ExpectedResult = "1")]
        [TestCase(SaveMergeRule.Or, "true", "0", ExpectedResult = "true")]
        [TestCase(SaveMergeRule.Or, "0", "0", ExpectedResult = "0")]
        // Union: account items first, in their order, then new guest items.
        [TestCase(SaveMergeRule.Union, "b\nc", "a\nb", ExpectedResult = "a\nb\nc")]
        [TestCase(SaveMergeRule.Union, null, null, ExpectedResult = null)]
        [TestCase(SaveMergeRule.PreferAccount, "guest", "acc", ExpectedResult = "acc")]
        [TestCase(SaveMergeRule.PreferAccount, "guest", null, ExpectedResult = "guest")]
        [TestCase(SaveMergeRule.PreferLocal, "guest", "acc", ExpectedResult = "guest")]
        [TestCase(SaveMergeRule.PreferLocal, null, "acc", ExpectedResult = "acc")]
        public string Apply(SaveMergeRule rule, string local, string account) =>
            SaveMergeRules.Apply(rule, local, account);

        [Test]
        public void Union_UsesTheGivenSeparator()
        {
            Assert.AreEqual("a,b", SaveMergeRules.Apply(SaveMergeRule.Union, "b", "a", ','));
        }

        [Test]
        public void Patterns_FirstMatchWins_DefaultForTheRest()
        {
            var rules = new SaveMergeRules(SaveMergeRule.PreferAccount)
                .Add("stat_best", SaveMergeRule.Max)
                .Add("stat_*", SaveMergeRule.PreferLocal)
                .Add("ach_*_unlocked", SaveMergeRule.Or);

            var local = Save(("stat_best", "50"), ("stat_tricks", "local"), ("ach_x_unlocked", "1"), ("colors", "guest"));
            var account = Save(("stat_best", "80"), ("stat_tricks", "acc"), ("colors", "acc"));

            Dictionary<string, string> merged = rules.Merge(local, account);

            Assert.AreEqual("80", merged["stat_best"]);
            Assert.AreEqual("local", merged["stat_tricks"]);
            Assert.AreEqual("1", merged["ach_x_unlocked"]);
            Assert.AreEqual("acc", merged["colors"]);
        }

        [Test]
        public void CustomRule_SeesBothSaves()
        {
            var rules = new SaveMergeRules()
                .Add("today", (key, input) => input.LocalValue("day") == input.AccountValue("day")
                    ? SaveMergeRules.Apply(SaveMergeRule.Max, input.LocalValue(key), input.AccountValue(key))
                    : input.AccountValue(key));

            var merged = rules.Merge(Save(("day", "5"), ("today", "90")), Save(("day", "5"), ("today", "10")));
            Assert.AreEqual("90", merged["today"]);

            merged = rules.Merge(Save(("day", "4"), ("today", "90")), Save(("day", "5"), ("today", "10")));
            Assert.AreEqual("10", merged["today"]);
        }
    }
}
