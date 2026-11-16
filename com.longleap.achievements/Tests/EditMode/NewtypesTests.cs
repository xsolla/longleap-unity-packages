using LongLeap.Achievements;
using NUnit.Framework;

namespace LongLeap.Achievements.Tests
{
    [TestFixture]
    public class AchievementIdTests
    {
        [Test]
        public void Value_ReturnsConstructedString()
        {
            var id = new AchievementId("ach_001");
            Assert.AreEqual("ach_001", id.Value);
        }

        [Test]
        public void None_Value_ReturnsNull()
        {
            Assert.IsNull(AchievementId.None.Value);
        }

        [Test]
        public void SameValue_AreEqual()
        {
            var a = new AchievementId("ach_001");
            var b = new AchievementId("ach_001");
            Assert.AreEqual(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Test]
        public void DifferentValue_AreNotEqual()
        {
            var a = new AchievementId("ach_001");
            var b = new AchievementId("ach_002");
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void None_IsNone_ReturnsTrue()
        {
            Assert.True(AchievementId.None.IsNone);
        }

        [Test]
        public void ConstructedWithValue_IsNone_ReturnsFalse()
        {
            var id = new AchievementId("test");
            Assert.False(id.IsNone);
        }

        [Test]
        public void GetHashCode_SameValue_AreEqual()
        {
            var a = new AchievementId("ach_001");
            var b = new AchievementId("ach_001");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void ToString_ReturnsWrappedValue()
        {
            var id = new AchievementId("ach_001");
            Assert.AreEqual("ach_001", id.ToString());
        }

        [Test]
        public void None_ToString_ReturnsEmptyString()
        {
            Assert.AreEqual("", AchievementId.None.ToString());
        }

        [Test]
        public void CaseSensitive_DifferentCasing_AreNotEqual()
        {
            var a = new AchievementId("Ach_001");
            var b = new AchievementId("ach_001");
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void EqualsObject_WhenOtherIsAchievementId_ReturnsCorrectResult()
        {
            var a = new AchievementId("id");
            object b = new AchievementId("id");
            object c = new AchievementId("other");
            Assert.True(a.Equals(b));
            Assert.False(a.Equals(c));
        }

        [Test]
        public void EqualsObject_WhenOtherIsNotAchievementId_ReturnsFalse()
        {
            var a = new AchievementId("id");
            Assert.False(a.Equals("id"));
        }

        [Test]
        public void OperatorEquals()
        {
            var a = new AchievementId("a");
            var b = new AchievementId("a");
            Assert.True(a == b);
            Assert.False(a == new AchievementId("b"));
        }

        [Test]
        public void OperatorNotEquals()
        {
            var a = new AchievementId("a");
            var b = new AchievementId("b");
            Assert.True(a != b);
            Assert.False(a != new AchievementId("a"));
        }
    }

    [TestFixture]
    public class GroupIdTests
    {
        [Test]
        public void Value_ReturnsConstructedString()
        {
            var id = new GroupId("combat");
            Assert.AreEqual("combat", id.Value);
        }

        [Test]
        public void SameValue_AreEqual()
        {
            var a = new GroupId("combat");
            var b = new GroupId("combat");
            Assert.AreEqual(a, b);
        }

        [Test]
        public void ImplicitFromString_CompilesAndMatches()
        {
            GroupId id = "combat";
            Assert.AreEqual(new GroupId("combat"), id);
        }

        [Test]
        public void None_IsNone_ReturnsTrue()
        {
            Assert.True(GroupId.None.IsNone);
        }

        [Test]
        public void ConstructedWithValue_IsNone_ReturnsFalse()
        {
            var id = new GroupId("exploration");
            Assert.False(id.IsNone);
        }

        [Test]
        public void GetHashCode_SameValue_AreEqual()
        {
            var a = new GroupId("combat");
            var b = new GroupId("combat");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void ToString_ReturnsWrappedValue()
        {
            var id = new GroupId("exploration");
            Assert.AreEqual("exploration", id.ToString());
        }

        [Test]
        public void OperatorEquals_WorksWithImplicitConversion()
        {
            GroupId id = "combat";
            Assert.True(id == "combat");
        }

        [Test]
        public void OperatorNotEquals_WorksWithImplicitConversion()
        {
            GroupId id = "combat";
            Assert.True(id != "exploration");
        }
    }

    [TestFixture]
    public class ObjectiveIdTests
    {
        [Test]
        public void Value_ReturnsConstructedString()
        {
            var id = new ObjectiveId("obj_001");
            Assert.AreEqual("obj_001", id.Value);
        }

        [Test]
        public void SameValue_AreEqual()
        {
            var a = new ObjectiveId("obj_001");
            var b = new ObjectiveId("obj_001");
            Assert.AreEqual(a, b);
        }

        [Test]
        public void GetHashCode_SameValue_AreEqual()
        {
            var a = new ObjectiveId("obj_001");
            var b = new ObjectiveId("obj_001");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void ToString_ReturnsWrappedValue()
        {
            var id = new ObjectiveId("obj_collect_gems");
            Assert.AreEqual("obj_collect_gems", id.ToString());
        }

        [Test]
        public void OperatorEquals()
        {
            var a = new ObjectiveId("x");
            var b = new ObjectiveId("x");
            Assert.True(a == b);
        }

        [Test]
        public void OperatorNotEquals()
        {
            var a = new ObjectiveId("x");
            var b = new ObjectiveId("y");
            Assert.True(a != b);
        }
    }

    [TestFixture]
    public class ProgressTests
    {
        [Test]
        public void Ratio_5of10_IsHalf()
        {
            var p = new Progress(5, 10);
            Assert.AreEqual(0.5f, p.Ratio);
        }

        [Test]
        public void IsComplete_10of10_ReturnsTrue()
        {
            var p = new Progress(10, 10);
            Assert.True(p.IsComplete);
        }

        [Test]
        public void IsComplete_5of10_ReturnsFalse()
        {
            var p = new Progress(5, 10);
            Assert.False(p.IsComplete);
        }

        [Test]
        public void Ratio_0Target_ReturnsZero()
        {
            var p = new Progress(5, 0);
            Assert.AreEqual(0f, p.Ratio);
        }

        [Test]
        public void SameValues_AreEqual()
        {
            var a = new Progress(5, 10);
            var b = new Progress(5, 10);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void DifferentValues_AreNotEqual()
        {
            var a = new Progress(5, 10);
            var b = new Progress(6, 10);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void Current_StoresConstructedValue()
        {
            var p = new Progress(7, 10);
            Assert.AreEqual(7, p.Current);
        }

        [Test]
        public void Target_StoresConstructedValue()
        {
            var p = new Progress(7, 10);
            Assert.AreEqual(10, p.Target);
        }

        [Test]
        public void IsComplete_OverTarget_ReturnsTrue()
        {
            var p = new Progress(15, 10);
            Assert.True(p.IsComplete);
        }

        [Test]
        public void Ratio_OverTarget_GreaterThanOne()
        {
            var p = new Progress(20, 10);
            Assert.AreEqual(2f, p.Ratio);
        }

        [Test]
        public void OperatorEquals()
        {
            var a = new Progress(1, 2);
            var b = new Progress(1, 2);
            Assert.True(a == b);
        }

        [Test]
        public void OperatorNotEquals()
        {
            var a = new Progress(1, 2);
            var b = new Progress(2, 2);
            Assert.True(a != b);
        }

        [Test]
        public void GetHashCode_SameValues_AreEqual()
        {
            var a = new Progress(5, 10);
            var b = new Progress(5, 10);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }
    }
}
