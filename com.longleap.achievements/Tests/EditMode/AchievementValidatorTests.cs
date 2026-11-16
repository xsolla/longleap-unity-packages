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

        private static AchievementDefinition RequiresDef(string id, params string[] required) =>
            new AchievementDefinition
            {
                Id = new AchievementId(id), Group = "g",
                Objectives = new[] { new Objective { Id = new ObjectiveId("o"), Required = required.Select(r => new AchievementId(r)).ToArray() } }
            };

        [Test]
        public void Validate_DanglingRequired_ReturnsWarning()
        {
            var result = AchievementValidator.Validate(new[] { RequiresDef("a", "nonexistent") });

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("required achievement 'nonexistent'")));
        }

        [Test]
        public void Validate_RequiredSelfReference_ReturnsError()
        {
            var result = AchievementValidator.Validate(new[] { RequiresDef("a", "a") });

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("'a' has itself")));
            Assert.IsFalse(result.Errors.Any(e => e.Contains("Circular")), "self-reference is reported once, not as a cycle too");
        }

        [Test]
        public void Validate_PrerequisiteSelfReference_ReturnsError()
        {
            var definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("a"), Group = "g", Prerequisites = new[] { new AchievementId("a") }, Objectives = Array.Empty<Objective>() }
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("'a' has itself as prerequisite")));
        }

        [Test]
        public void Validate_CyclicRequired_ReturnsError()
        {
            var result = AchievementValidator.Validate(new[] { RequiresDef("a", "b"), RequiresDef("b", "a") });

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("Circular")));
        }

        [Test]
        public void Validate_CycleMixingPrerequisitesAndRequired_ReturnsError()
        {
            var definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("a"), Group = "g", Prerequisites = new[] { new AchievementId("b") }, Objectives = Array.Empty<Objective>() },
                RequiresDef("b", "a")
            };

            var result = AchievementValidator.Validate(definitions);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("Circular")));
        }

        [Test]
        public void Validate_NullObjectives_DoesNotThrow()
        {
            var definitions = new[]
            {
                new AchievementDefinition { Id = new AchievementId("a"), Group = "g", Prerequisites = new[] { new AchievementId("b") } },
                new AchievementDefinition { Id = new AchievementId("b"), Group = "g" }
            };

            ValidationResult result = null;
            Assert.DoesNotThrow(() => result = AchievementValidator.Validate(definitions));
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_NullEntriesAndMissingId_ReturnErrors()
        {
            var definitions = new[]
            {
                null,
                new AchievementDefinition { Group = "g", Objectives = Array.Empty<Objective>() },
                new AchievementDefinition
                {
                    Id = new AchievementId("a"), Group = "g",
                    Objectives = new Objective[] { null },
                    Prerequisites = null
                }
            };

            ValidationResult result = null;
            Assert.DoesNotThrow(() => result = AchievementValidator.Validate(definitions));
            Assert.IsTrue(result.Errors.Any(e => e.Contains("index 0 is null")));
            Assert.IsTrue(result.Errors.Any(e => e.Contains("index 1 has no ID")));
            Assert.IsTrue(result.Errors.Any(e => e.Contains("'a' has a null objective")));
        }
    }
}
