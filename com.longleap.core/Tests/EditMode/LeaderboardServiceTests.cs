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
