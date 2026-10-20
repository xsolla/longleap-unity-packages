using System.Collections.Generic;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;

namespace LongLeap.Accounts.Tests
{
    public class SaveMergeRulesTests
    {
        private static Dictionary<string, CloudSaveEntry> Save(params (string key, string value)[] pairs)
        {
            var save = new Dictionary<string, CloudSaveEntry>();

            foreach (var (key, value) in pairs)
                save[key] = new CloudSaveEntry(value, 100);

            return save;
        }

        [Test]
        public void Max_TakesTheLargerNumber()
        {
            Assert.AreEqual("7", SaveMergeRules.Apply(SaveMergeRule.Max, "7", "3"));
            Assert.AreEqual("9", SaveMergeRules.Apply(SaveMergeRule.Max, "7", "9"));
            Assert.AreEqual("1.5", SaveMergeRules.Apply(SaveMergeRule.Max, "1.5", "1.25"));
        }

        [Test]
        public void Max_MissingOrGarbageSideLoses()
        {
            Assert.AreEqual("4", SaveMergeRules.Apply(SaveMergeRule.Max, null, "4"));
            Assert.AreEqual("4", SaveMergeRules.Apply(SaveMergeRule.Max, "4", null));
            Assert.AreEqual("4", SaveMergeRules.Apply(SaveMergeRule.Max, "junk", "4"));
            Assert.AreEqual("junk", SaveMergeRules.Apply(SaveMergeRule.Max, "other", "junk"), "neither parses — keep the account's");
        }

        [Test]
        public void Min_IgnoresZeroAsUnknown()
        {
            Assert.AreEqual("100", SaveMergeRules.Apply(SaveMergeRule.Min, "100", "200"));
            Assert.AreEqual("200", SaveMergeRules.Apply(SaveMergeRule.Min, "0", "200"));
            Assert.AreEqual("200", SaveMergeRules.Apply(SaveMergeRule.Min, null, "200"));
        }

        [Test]
        public void Or_IsSetIfEitherIsSet()
        {
            Assert.AreEqual("1", SaveMergeRules.Apply(SaveMergeRule.Or, "1", null));
            Assert.AreEqual("1", SaveMergeRules.Apply(SaveMergeRule.Or, "0", "1"));
            Assert.AreEqual("true", SaveMergeRules.Apply(SaveMergeRule.Or, "true", "0"));
            Assert.AreEqual("0", SaveMergeRules.Apply(SaveMergeRule.Or, "0", "0"));
        }

        [Test]
        public void Union_KeepsAccountOrderAndAddsGuestItems()
        {
            Assert.AreEqual("a\nb\nc", SaveMergeRules.Apply(SaveMergeRule.Union, "b\nc", "a\nb"));
            Assert.AreEqual("a,b", SaveMergeRules.Apply(SaveMergeRule.Union, "b", "a", ','));
            Assert.IsNull(SaveMergeRules.Apply(SaveMergeRule.Union, null, null));
        }

        [Test]
        public void PreferAccount_FallsBackToGuest()
        {
            Assert.AreEqual("acc", SaveMergeRules.Apply(SaveMergeRule.PreferAccount, "guest", "acc"));
            Assert.AreEqual("guest", SaveMergeRules.Apply(SaveMergeRule.PreferAccount, "guest", null));
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
