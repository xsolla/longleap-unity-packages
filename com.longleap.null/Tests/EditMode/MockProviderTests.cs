using NUnit.Framework;
using LongLeap.Core.Leaderboards;
using LongLeap.Null;
using System.Threading.Tasks;

namespace LongLeap.Null.Tests
{
    [TestFixture]
    public class MockProviderTests
    {
        private NullLeaderboardProvider _provider;
        private BoardId _boardId;

        [SetUp]
        public void Setup()
        {
            _provider = new NullLeaderboardProvider();
            _boardId = new BoardId("kills");
        }

        [Test]
        public async Task SubmitScore_NewEntry_ReturnsTrue()
        {
            var result = await _provider.SubmitScoreAsync(_boardId, "uid1", 100, "Player_1", SortDirection.Descending);
            Assert.IsTrue(result);
        }

        [Test]
        public async Task GetTopAsync_EmptyBoard_ReturnsEmptyPage()
        {
            var page = await _provider.GetTopAsync(_boardId, SortDirection.Descending, 10);
            Assert.AreEqual(0, page.Entries.Count);
            Assert.IsFalse(page.HasMore);
        }

        [Test]
        public async Task GetPlayerRankAsync_UnknownUid_ReturnsNullRank()
        {
            var rank = await _provider.GetPlayerRankAsync(_boardId, SortDirection.Descending, "unknown");
            Assert.IsNull(rank.Rank);
            Assert.IsNull(rank.Score);
        }

        [Test]
        public void Dispose_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _provider.Dispose());
        }
    }
}
