using System.Threading.Tasks;
using LongLeap.XsollaId.Leaderboards;
using LongLeap.XsollaId.Leagues;
using NUnit.Framework;

namespace LongLeap.Null.Tests
{
    // Null backends of the leaderboard and league seams. Those seams exist only with
    // XSOLLA_ID (they live in LongLeap.XsollaId.Web), so these tests do too.
    [TestFixture]
    public class NullXsollaIdProvidersTests
    {
        [Test]
        public void Leaderboard_AcceptsSubmitsAndReturnsEmptyBoards()
        {
            var provider = new NullLeaderboardProvider();
            var board = new BoardId("kills");
            const SortDirection sort = SortDirection.Descending;

            AssertDone(provider.SubmitScoreAsync(board, "uid", 100, "Player", sort), out bool accepted);
            AssertDone(provider.GetTopAsync(board, sort, 10), out LeaderboardPage top);
            AssertDone(provider.GetSurroundingsAsync(board, sort, "uid", 5), out LeaderboardPage around);
            AssertDone(provider.GetPlayerRankAsync(board, sort, "uid"), out PlayerRank rank);

            Assert.IsTrue(accepted);
            Assert.AreEqual(0, top.Entries.Count);
            Assert.IsFalse(top.HasMore);
            Assert.AreEqual(0, around.Entries.Count);
            Assert.IsNull(rank.Rank);
            Assert.IsNull(rank.Score);
            Assert.AreEqual(0, rank.TotalPlayers);
            Assert.DoesNotThrow(provider.Dispose);
        }

        [Test]
        public void League_ReturnsEmptyStateAndNoReward()
        {
            var provider = new NullLeagueProvider();

            AssertDone(provider.GetStateAsync("weekly"), out var state);
            AssertDone(provider.SubmitScoreAsync("weekly", 100, "Player"), out var afterSubmit);
            AssertDone(provider.ClaimAsync("weekly", 1), out var claim);
            AssertDone(provider.DebugFillGroupAsync("weekly"), out var filled);

            Assert.AreEqual("weekly", state.LeagueId);
            Assert.IsFalse(state.IsJoined);
            Assert.AreEqual("weekly", afterSubmit.LeagueId);
            Assert.IsFalse(claim.Claimed);
            Assert.IsNull(filled);
            Assert.IsTrue(provider.DebugClosePeriodAsync("weekly").IsCompleted);
            Assert.DoesNotThrow(provider.Dispose);
        }

        private static void AssertDone<T>(Task<T> task, out T result)
        {
            Assert.IsTrue(task.IsCompleted, "null providers must complete synchronously");
            result = task.Result;
        }
    }
}
