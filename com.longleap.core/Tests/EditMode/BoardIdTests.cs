using NUnit.Framework;
using LongLeap.Core.Leaderboards;
using System;
using UnityEngine.TestTools;

namespace LongLeap.Core.Tests
{
    [TestFixture]
    public class BoardIdTests
    {
        [Test]
        public void ValidId_StoresValue()
        {
            var id = new BoardId("kills");
            Assert.AreEqual("kills", id.Value);
            Assert.IsTrue(id.IsValid);
        }

        [Test]
        public void ValidId_WithHyphen_Passes()
        {
            var id = new BoardId("survival-time");
            Assert.AreEqual("survival-time", id.Value);
            Assert.IsTrue(id.IsValid);
        }

        [Test]
        public void ValidId_WithUnderscore_Passes()
        {
            var id = new BoardId("survival_time");
            Assert.AreEqual("survival_time", id.Value);
            Assert.IsTrue(id.IsValid);
        }

        [Test]
        public void ValidId_Alphanumeric_Passes()
        {
            var id = new BoardId("board42");
            Assert.AreEqual("board42", id.Value);
            Assert.IsTrue(id.IsValid);
        }

        [Test]
        public void ValidId_MaxLength39_Passes()
        {
            var id = new BoardId(new string('a', 39));
            Assert.IsTrue(id.IsValid);
        }

        [Test]
        public void InvalidId_Empty_Fails()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, "BoardId: invalid identifier ''. Using 'invalid_board_id'.");
            var id = new BoardId("");
            Assert.IsFalse(id.IsValid);
            Assert.AreEqual("invalid_board_id", id.Value);
        }

        [Test]
        public void InvalidId_Null_Fails()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, "BoardId: invalid identifier ''. Using 'invalid_board_id'.");
            var id = new BoardId(null);
            Assert.IsFalse(id.IsValid);
        }

        [Test]
        public void InvalidId_TooLong_Fails()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, "BoardId: invalid identifier 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'. Using 'invalid_board_id'.");
            var id = new BoardId(new string('a', 41));
            Assert.IsFalse(id.IsValid);
        }

        [Test]
        public void InvalidId_DigitFirst_Fails()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, "BoardId: invalid identifier '1kills'. Using 'invalid_board_id'.");
            var id = new BoardId("1kills");
            Assert.IsFalse(id.IsValid);
        }

        [Test]
        public void InvalidId_Cyrillic_Fails()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, "BoardId: invalid identifier 'очки'. Using 'invalid_board_id'.");
            var id = new BoardId("очки");
            Assert.IsFalse(id.IsValid);
        }

        [Test]
        public void InvalidId_Space_Fails()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, "BoardId: invalid identifier 'my board'. Using 'invalid_board_id'.");
            var id = new BoardId("my board");
            Assert.IsFalse(id.IsValid);
        }

        [Test]
        public void None_ReturnsSentinel()
        {
            Assert.IsTrue(BoardId.None.IsNone);
        }

        [Test]
        public void Equals_SameValue_ReturnsTrue()
        {
            var a = new BoardId("kills");
            var b = new BoardId("kills");
            Assert.AreEqual(a, b);
            Assert.True(a == b);
        }

        [Test]
        public void Equals_DifferentValue_ReturnsFalse()
        {
            var a = new BoardId("kills");
            var b = new BoardId("score");
            Assert.AreNotEqual(a, b);
            Assert.True(a != b);
        }

        [Test]
        public void Equals_CaseSensitive_ReturnsFalse()
        {
            var a = new BoardId("Kills");
            var b = new BoardId("kills");
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void GetHashCode_SameValue_Matches()
        {
            var a = new BoardId("kills");
            var b = new BoardId("kills");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void ToString_ReturnsValue()
        {
            var id = new BoardId("kills");
            Assert.AreEqual("kills", id.ToString());
        }
    }
}
