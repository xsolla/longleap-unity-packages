using NUnit.Framework;
using LongLeap.Core.Auth;
using LongLeap.Core.Leaderboards;
using LongLeap.Null;
using System.Threading.Tasks;

namespace LongLeap.Core.Tests
{
    [TestFixture]
    public class LeaderboardServiceTests
    {
        private NullLeaderboardProvider _provider;
        private NullAuthProvider _auth;
        private LeaderboardService _service;
        private BoardId _killBoard;
        private BoardId _timeBoard;

        [SetUp]
        public void Setup()
        {
            _provider = new NullLeaderboardProvider();
            _auth = new NullAuthProvider();
            _killBoard = new BoardId("kills");
            _timeBoard = new BoardId("survival_time");

            _service = new LeaderboardService("test_", _provider, _auth);
        }

        [Test]
        public void GetBoard_KnownBoard_ReturnsHandle()
        {
            var board = _service.GetBoard(_killBoard, SortDirection.Descending);
            Assert.IsNotNull(board);
            Assert.AreEqual(_killBoard, board.Id);
        }

        [Test]
        public void GetBoard_SameBoardTwice_ReturnsSameInstance()
        {
            var a = _service.GetBoard(_killBoard, SortDirection.Descending);
            var b = _service.GetBoard(_killBoard, SortDirection.Descending);
            Assert.AreSame(a, b);
        }

        [Test]
        public async Task SubmitScoreAsync_ReturnsAccepted()
        {
            var board = _service.GetBoard(_killBoard, SortDirection.Descending);
            var result = await board.SubmitScoreAsync(100);
            Assert.IsTrue(result.IsAccepted);
            Assert.IsTrue(result.IsNewBest);
            Assert.AreEqual(100, result.Score);
        }

        [Test]
        public async Task FlushAsync_SendsToProvider()
        {
            var board = _service.GetBoard(_killBoard, SortDirection.Descending);
            await board.SubmitScoreAsync(100);
            await _service.FlushAsync();

            var page = await _provider.GetTopAsync(_killBoard, SortDirection.Descending, 10);
            Assert.AreEqual(0, page.Entries.Count);
        }

        [Test]
        public void Dispose_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.Dispose());
        }

        [Test]
        public async Task Dispose_ThenSubmit_ReturnsNotAccepted()
        {
            var board = _service.GetBoard(_killBoard, SortDirection.Descending);
            _service.Dispose();

            var result = await board.SubmitScoreAsync(100);
            Assert.IsFalse(result.IsAccepted);
        }

        // Regression: with a prefix (Editor, development builds) an accepted
        // submit did not invalidate the cached top, so the board kept showing
        // what it had before the run until the scene reloaded.
        [Test]
        public async Task FlushAsync_AcceptedScore_RefreshesCachedTop_WithPrefix()
        {
            var provider = new RecordingProvider();
            var service = new LeaderboardService("dev_", provider, _auth);
            var board = service.GetBoard(_killBoard, SortDirection.Descending);

            var before = await board.GetTopAsync(10);
            Assert.AreEqual(0, before.Entries.Count, "the board starts empty and that result is cached");

            await board.SubmitScoreAsync(100);
            await service.FlushAsync();

            var after = await board.GetTopAsync(10);
            Assert.AreEqual(1, after.Entries.Count, "the accepted score must replace the cached empty top");
            Assert.AreEqual(100, after.Entries[0].Score);
            Assert.AreEqual("dev_kills", provider.LastBoard.Value, "the provider gets the prefixed id");
        }

        // Keeps submitted scores and serves them back as the top.
        private sealed class RecordingProvider : ILeaderboardProvider
        {
            private readonly System.Collections.Generic.List<LeaderboardEntry> _entries =
                new System.Collections.Generic.List<LeaderboardEntry>();

            public BoardId LastBoard { get; private set; }

            public Task<bool> SubmitScoreAsync(BoardId boardId, string uid, long score, string displayName,
                SortDirection sort)
            {
                LastBoard = boardId;
                _entries.Add(new LeaderboardEntry(uid, displayName, score, _entries.Count + 1, 0));
                return Task.FromResult(true);
            }

            public Task<LeaderboardPage> GetTopAsync(BoardId boardId, SortDirection sort, int count) =>
                Task.FromResult(new LeaderboardPage(
                    new System.Collections.Generic.List<LeaderboardEntry>(_entries), null, false));

            public Task<PlayerRank> GetPlayerRankAsync(BoardId boardId, SortDirection sort, string uid) =>
                Task.FromResult(new PlayerRank(_entries.Count > 0 ? 1 : (int?)null, null, _entries.Count));

            public Task<LeaderboardPage> GetSurroundingsAsync(BoardId boardId, SortDirection sort, string uid,
                int radius) =>
                Task.FromResult(LeaderboardPage.Empty());

            public void Dispose()
            {
            }
        }

        private sealed class TestConfig
        {
            public bool UseMockProvider { get; }
            public string LeaderboardPrefix { get; }

            public TestConfig(bool useMock, string leaderboardPrefix)
            {
                UseMockProvider = useMock;
                LeaderboardPrefix = leaderboardPrefix;
            }
        }
    }
}
