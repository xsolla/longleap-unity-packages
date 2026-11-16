using LongLeap.Achievements;
using NUnit.Framework;

namespace LongLeap.Achievements.Tests
{
    [TestFixture]
    public class AchievementDefinitionTests
    {
        [Test]
        public void InitProperties_StoresCorrectValues()
        {
            var def = new AchievementDefinition
            {
                Id = new AchievementId("ach_kill"),
                Group = new GroupId("combat"),
                IsHidden = true,
                Prerequisites = new[] { new AchievementId("ach_prereq") },
                Objectives = new[]
                {
                    new Objective { Id = new ObjectiveId("obj_kill"), StatPath = "kill", StatTarget = 10 }
                }
            };

            Assert.AreEqual(new AchievementId("ach_kill"), def.Id);
            Assert.AreEqual(new GroupId("combat"), def.Group);
            Assert.IsTrue(def.IsHidden);
            Assert.AreEqual(1, def.Prerequisites.Length);
            Assert.AreEqual(new AchievementId("ach_prereq"), def.Prerequisites[0]);
            Assert.AreEqual(1, def.Objectives.Length);
            Assert.AreEqual("kill", def.Objectives[0].StatPath);
            Assert.AreEqual(10, def.Objectives[0].StatTarget);
        }
    }

    [TestFixture]
    public class AchievementStatusTests
    {
        [Test]
        public void ContainsDefinitionAndProgress()
        {
            var def = new AchievementDefinition
            {
                Id = new AchievementId("ach_kill"),
                Group = new GroupId("combat"),
                Objectives = new[]
                {
                    new Objective { Id = new ObjectiveId("obj_kill"), StatPath = "kill", StatTarget = 10 }
                }
            };

            var status = new AchievementStatus
            {
                Definition = def,
                IsAvailable = true,
                IsUnlocked = false,
                ObjectiveProgresses = new[]
                {
                    new ObjectiveProgress
                    {
                        ObjectiveId = new ObjectiveId("obj_kill"),
                        Progress = new Progress(5, 10)
                    }
                }
            };

            Assert.AreEqual(def, status.Definition);
            Assert.IsTrue(status.IsAvailable);
            Assert.IsFalse(status.IsUnlocked);
            Assert.AreEqual(1, status.ObjectiveProgresses.Length);
            Assert.AreEqual(new ObjectiveId("obj_kill"), status.ObjectiveProgresses[0].ObjectiveId);
            Assert.AreEqual(new Progress(5, 10), status.ObjectiveProgresses[0].Progress);
        }

        [Test]
        public void IsUnlocked_WhenUnlocked()
        {
            var status = new AchievementStatus
            {
                Definition = new AchievementDefinition { Id = new AchievementId("ach"), Group = "test" },
                IsAvailable = true,
                IsUnlocked = true,
                ObjectiveProgresses = new[]
                {
                    new ObjectiveProgress
                    {
                        ObjectiveId = new ObjectiveId("o"),
                        Progress = new Progress(10, 10)
                    }
                }
            };

            Assert.IsTrue(status.IsUnlocked);
        }
    }

    [TestFixture]
    public class SnapshotSerializationTests
    {
        [Test]
        public void RoundTrip_SimpleSnapshot()
        {
            var snapshot = new AchievementStatesSnapshot
            {
                Version = 1,
                Entries = new[]
                {
                    new AchievementStateEntry
                    {
                        AchievementId = "ach_kill",
                        IsUnlocked = true,
                        UnlockedTimestamp = 123456789L
                    }
                }
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(snapshot);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<AchievementStatesSnapshot>(json);

            Assert.NotNull(deserialized);
            Assert.AreEqual(1, deserialized.Version);
            Assert.AreEqual(1, deserialized.Entries.Length);
            Assert.AreEqual("ach_kill", deserialized.Entries[0].AchievementId);
            Assert.IsTrue(deserialized.Entries[0].IsUnlocked);
            Assert.AreEqual(123456789L, deserialized.Entries[0].UnlockedTimestamp);
        }

        [Test]
        public void RoundTrip_EmptyEntries()
        {
            var snapshot = new AchievementStatesSnapshot
            {
                Version = 1,
                Entries = System.Array.Empty<AchievementStateEntry>()
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(snapshot);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<AchievementStatesSnapshot>(json);

            Assert.NotNull(deserialized);
            Assert.AreEqual(0, deserialized.Entries.Length);
        }
    }
}
