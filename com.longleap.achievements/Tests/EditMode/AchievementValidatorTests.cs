using LongLeap.Achievements;
using NUnit.Framework;
using System;
using System.Linq;

namespace LongLeap.Achievements.Tests
{
    [TestFixture]
    public class AchievementValidatorTests
    {
        [Test]
        public void Validate_DuplicateAchievementId_ReturnsError()
        {
            var definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("dup_ach"), Group = "g1", Objectives = Array.Empty<Objective>() },
                new AchievementDefinition { Id = new AchievementId("dup_ach"), Group = "g2", Objectives = Array.Empty<Objective>() }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("dup_ach")));
        }

        [Test]
        public void Validate_CyclicPrerequisites_ReturnsError()
        {
            var definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("a"), Group = "g", Prerequisites = new[] { new AchievementId("b") }, Objectives = Array.Empty<Objective>() },
                new AchievementDefinition { Id = new AchievementId("b"), Group = "g", Prerequisites = new[] { new AchievementId("a") }, Objectives = Array.Empty<Objective>() }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("Circular", StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public void Validate_DanglingPrerequisite_ReturnsWarning()
        {
            var definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("a"), Group = "g", Prerequisites = new[] { new AchievementId("nonexistent") }, Objectives = Array.Empty<Objective>() }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("nonexistent")));
        }

        [Test]
        public void Validate_ZeroObjectivesAndZeroPrerequisites_ReturnsWarning()
        {
            var definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("empty_ach"), Group = "g", Objectives = Array.Empty<Objective>() }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("empty_ach")));
        }

        [Test]
        public void Validate_StatObjective_Valid()
        {
            var definitions = new[]
            {
                new AchievementDefinition
                {
                    Id = new AchievementId("ach_valid"), Group = "g",
                    Objectives = new[] { new Objective { Id = new ObjectiveId("o1"), StatPath = "kill", StatTarget = 10 } }
                }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsTrue(result.IsValid);
            Assert.IsEmpty(result.Errors);
        }

        [Test]
        public void Validate_RequiredObjective_Valid()
        {
            var definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("a"), Group = "g", Objectives = Array.Empty<Objective>() },
                new AchievementDefinition
                {
                    Id = new AchievementId("b"), Group = "g",
                    Objectives = new[] { new Objective { Id = new ObjectiveId("o1"), Required = new[] { new AchievementId("a") } } }
                }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsTrue(result.IsValid);
            Assert.IsEmpty(result.Errors);
        }

        [Test]
        public void Validate_ObjectiveNoStatPathNoRequired_ReturnsError()
        {
            var definitions = new[]
            {
                new AchievementDefinition
                {
                    Id = new AchievementId("bad"), Group = "g",
                    Objectives = new[] { new Objective { Id = new ObjectiveId("o1") } }
                }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("o1") && e.Contains("no StatPath")));
        }

        [Test]
        public void Validate_StatTargetZero_ReturnsError()
        {
            var definitions = new[]
            {
                new AchievementDefinition
                {
                    Id = new AchievementId("bad"), Group = "g",
                    Objectives = new[] { new Objective { Id = new ObjectiveId("o1"), StatPath = "kill", StatTarget = 0 } }
                }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("StatTarget") && e.Contains("<= 0")));
        }

        [Test]
        public void Validate_DuplicateObjectiveId_ReturnsError()
        {
            var definitions = new[]
            {
                new AchievementDefinition
                {
                    Id = new AchievementId("ach"), Group = "g",
                    Objectives = new[]
                    {
                        new Objective { Id = new ObjectiveId("dup"), StatPath = "kill", StatTarget = 5 },
                        new Objective { Id = new ObjectiveId("dup"), StatPath = "xp", StatTarget = 10 }
                    }
                }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("dup")));
        }

        [Test]
        public void Validate_NullDefinitions_ReturnsValid()
        {
            var result = AchievementValidator.Validate(null);
            Assert.IsTrue(result.IsValid);
            Assert.IsEmpty(result.Errors);
        }

        [Test]
        public void Validate_EmptyDefinitions_ReturnsValid()
        {
            var result = AchievementValidator.Validate(Array.Empty<AchievementDefinition>());
            Assert.IsTrue(result.IsValid);
            Assert.IsEmpty(result.Errors);
        }
    }
}
