using NUnit.Framework;
using System;
using LongLeap.Stats;

namespace LongLeap.Stats.Tests
{
    [TestFixture]
    public class StatPathTests
    {
        [Test]
        public void Constructor_TrimsAndLowercases()
        {
            var path = new StatPath("  Kill:Monk  ");
            Assert.AreEqual("kill:monk", path.Value);
        }

        [Test]
        public void Constructor_ThrowsOnEmpty()
        {
            Assert.Throws<ArgumentException>(() => new StatPath(""));
        }

        [Test]
        public void Constructor_ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => new StatPath(null));
        }

        [Test]
        public void Constructor_ThrowsOnDoubleColon()
        {
            Assert.Throws<ArgumentException>(() => new StatPath("kill::monk"));
        }

        [Test]
        public void Constructor_ThrowsOnLeadingColon()
        {
            Assert.Throws<ArgumentException>(() => new StatPath(":kill"));
        }

        [Test]
        public void Constructor_ThrowsOnTrailingColon()
        {
            Assert.Throws<ArgumentException>(() => new StatPath("kill:"));
        }

        [Test]
        public void Depth_SingleSegment_ReturnsOne()
        {
            Assert.AreEqual(1, new StatPath("kill").Depth);
        }

        [Test]
        public void Depth_MultiSegment_ReturnsCorrectCount()
        {
            Assert.AreEqual(3, new StatPath("kill:monk:zombie").Depth);
        }

        [Test]
        public void LastSegment_SingleSegment_ReturnsSelf()
        {
            Assert.AreEqual("kill", new StatPath("kill").LastSegment);
        }

        [Test]
        public void LastSegment_MultiSegment_ReturnsFinal()
        {
            Assert.AreEqual("zombie", new StatPath("kill:monk:zombie").LastSegment);
        }

        [Test]
        public void IsPattern_WithStar_ReturnsTrue()
        {
            Assert.IsTrue(new StatPath("kill:*:grunt").IsPattern);
        }

        [Test]
        public void IsPattern_WithoutStar_ReturnsFalse()
        {
            Assert.IsFalse(new StatPath("kill:monk:grunt").IsPattern);
        }

        [Test]
        public void Parent_RootPath_ReturnsNone()
        {
            Assert.IsTrue(new StatPath("kill").Parent.IsNone);
        }

        [Test]
        public void Parent_DeepPath_ReturnsParent()
        {
            var parent = new StatPath("kill:monk:grunt").Parent;
            Assert.AreEqual("kill:monk", parent.Value);
        }

        [Test]
        public void Append_ValidSegment_ReturnsCombined()
        {
            var path = new StatPath("kill").Append("monk");
            Assert.AreEqual("kill:monk", path.Value);
        }

        [Test]
        public void Append_ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => new StatPath("kill").Append(null));
        }

        [Test]
        public void Append_ThrowsOnEmpty()
        {
            Assert.Throws<ArgumentException>(() => new StatPath("kill").Append(""));
        }

        [Test]
        public void Append_ThrowsOnColon()
        {
            Assert.Throws<ArgumentException>(() => new StatPath("kill").Append("monk:zombie"));
        }

        [Test]
        public void Matches_Exact_ReturnsTrue()
        {
            Assert.IsTrue(new StatPath("kill:monk").Matches(new StatPath("kill:monk")));
        }

        [Test]
        public void Matches_Exact_ReturnsFalse()
        {
            Assert.IsFalse(new StatPath("kill:monk").Matches(new StatPath("kill:ninja")));
        }

        [Test]
        public void Matches_Wildcard_SingleSegment()
        {
            Assert.IsTrue(new StatPath("kill:*:grunt").Matches(new StatPath("kill:monk:grunt")));
        }

        [Test]
        public void Matches_Wildcard_RejectsWrongSegment()
        {
            Assert.IsFalse(new StatPath("kill:*:grunt").Matches(new StatPath("kill:monk:brute")));
        }

        [Test]
        public void Matches_Wildcard_DifferentDepth()
        {
            Assert.IsFalse(new StatPath("kill:*").Matches(new StatPath("kill:monk:grunt")));
        }

        [Test]
        public void Matches_WildcardMultiple_AllSegments()
        {
            Assert.IsTrue(new StatPath("*:*:*").Matches(new StatPath("kill:monk:grunt")));
        }

        [Test]
        public void Matches_NonePath_ReturnsFalse()
        {
            Assert.IsFalse(new StatPath("kill").Matches(default(StatPath)));
        }

        [Test]
        public void Equals_SameValue_ReturnsTrue()
        {
            Assert.AreEqual(new StatPath("kill:monk"), new StatPath("kill:monk"));
        }

        [Test]
        public void Equals_DifferentValue_ReturnsFalse()
        {
            Assert.AreNotEqual(new StatPath("kill:monk"), new StatPath("kill:ninja"));
        }

        [Test]
        public void GetHashCode_SameValue_Equal()
        {
            Assert.AreEqual(
                new StatPath("kill:monk").GetHashCode(),
                new StatPath("kill:monk").GetHashCode());
        }

        [Test]
        public void ImplicitConversion_FromString()
        {
            StatPath path = "kill:monk";
            Assert.AreEqual("kill:monk", path.Value);
        }

        [Test]
        public void ToString_ReturnsValue()
        {
            var path = new StatPath("kill");
            Assert.AreEqual("kill", path.ToString());
        }

        [Test]
        public void IsNone_Default_ReturnsTrue()
        {
            Assert.IsTrue(default(StatPath).IsNone);
        }

        [Test]
        public void IsNone_Constructed_ReturnsFalse()
        {
            Assert.IsFalse(new StatPath("kill").IsNone);
        }

        [Test]
        public void OperatorEquals()
        {
            Assert.IsTrue(new StatPath("a") == new StatPath("a"));
            Assert.IsFalse(new StatPath("a") == new StatPath("b"));
        }

        [Test]
        public void OperatorNotEquals()
        {
            Assert.IsTrue(new StatPath("a") != new StatPath("b"));
            Assert.IsFalse(new StatPath("a") != new StatPath("a"));
        }
    }
}
