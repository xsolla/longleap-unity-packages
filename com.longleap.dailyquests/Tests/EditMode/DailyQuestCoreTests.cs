using System;
using LongLeap.Stats;
using LongLeap.Stats.Tests;
using NUnit.Framework;

namespace LongLeap.DailyQuests.Tests
{
    [TestFixture]
    public class ProgressEvaluatorTests
    {
        private static readonly StatPath Grind = new("grind");

        private FakeStatSystem _stats;
        private ProgressEvaluator _progress;

        [SetUp]
        public void Setup()
        {
            _stats = new FakeStatSystem();
            _stats.Define(Grind, StatType.Value);
            _progress = new ProgressEvaluator(_stats);
        }

        private static QuestDefinition Quest(QuestProgressMode mode, int target = 50) =>
            new QuestDefinition
            {
                Id = new QuestId("q"),
                Objectives = new[] { new QuestObjective { Id = new QuestObjectiveId("o0"), StatPath = "grind", StatTarget = target, Mode = mode } }
            };

        [Test]
        public void Delta_StatBelowBaseline_LowersBaseline()
        {
            var quest = Quest(QuestProgressMode.Delta);
            _stats.Set(Grind, 100);
            var entry = _progress.CreateEntry(quest);

            _stats.Set(Grind, 30);
            bool dirty = _progress.Evaluate(entry, quest, out bool completed);

            Assert.IsTrue(dirty, "the new baseline must be saved");
            Assert.IsFalse(completed);
            Assert.AreEqual(30, entry.Baselines[0].Value);

            _stats.Set(Grind, 45);
            Assert.AreEqual(new QuestProgress(15, 50), _progress.Compute(entry, quest.Objectives[0]));
        }

        [Test]
        public void Delta_StatAboveBaseline_KeepsBaseline()
        {
            var quest = Quest(QuestProgressMode.Delta);
            _stats.Set(Grind, 100);
            var entry = _progress.CreateEntry(quest);

            _stats.Set(Grind, 120);

            Assert.IsFalse(_progress.Evaluate(entry, quest, out _));
            Assert.AreEqual(100, entry.Baselines[0].Value);
        }

        [Test]
        public void Absolute_StatDrops_IsNotRebased()
        {
            var quest = Quest(QuestProgressMode.Absolute);
            _stats.Set(Grind, 100);
            var entry = _progress.CreateEntry(quest);

            _stats.Set(Grind, 30);

            Assert.IsFalse(_progress.Evaluate(entry, quest, out _));
            Assert.AreEqual(100, entry.Baselines[0].Value);
        }

        [Test]
        public void Compute_DoesNotChangeState()
        {
            var quest = Quest(QuestProgressMode.Delta);
            _stats.Set(Grind, 100);
            var entry = _progress.CreateEntry(quest);

            _stats.Set(Grind, 30);
            _progress.Compute(entry, quest.Objectives[0]);

            Assert.AreEqual(100, entry.Baselines[0].Value);
        }

        [Test]
        public void Evaluate_TargetReached_LatchesObjectiveAndQuest()
        {
            var quest = Quest(QuestProgressMode.Delta);
            var entry = _progress.CreateEntry(quest);

            _stats.Set(Grind, 50);

            Assert.IsTrue(_progress.Evaluate(entry, quest, out bool completed));
            Assert.IsTrue(completed);
            Assert.IsTrue(entry.IsCompleted);
            Assert.IsTrue(entry.Baselines[0].IsCompleted);
            Assert.IsFalse(_progress.Evaluate(entry, quest, out completed), "a second call changes nothing");
            Assert.IsFalse(completed);
        }
    }
}
