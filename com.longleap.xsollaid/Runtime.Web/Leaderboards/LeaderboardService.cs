using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.Auth;
using UnityEngine;

namespace LongLeap.XsollaId.Leaderboards
{
    public sealed class LeaderboardService : ILeaderboardService, IDisposable
    {
        private const int TopRankThreshold = 10;

        private readonly string _prefix;
        private readonly ILeaderboardProvider _provider;
        private readonly IAuthProvider _auth;
        private readonly Func<string> _displayNameResolver;
        private readonly Dictionary<BoardId, LeaderboardHandle> _handles = new();
        private readonly Dictionary<BoardId, (long score, SortDirection sort)> _batcher = new();
        private readonly Dictionary<BoardId, LeaderboardPage> _topCache = new();
        private readonly Dictionary<BoardId, LeaderboardPage> _surroundingsCache = new();
        private readonly object _lock = new();
        private bool _disposed;
        private string _cachedUid;

        public event Action<BoardId, int> OnTopRankAchieved;

        public LeaderboardService(string prefix, ILeaderboardProvider provider, IAuthProvider auth,
            Func<string> displayNameResolver = null)
        {
            _prefix = prefix ?? string.Empty;
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _displayNameResolver = displayNameResolver ?? (() => "Player");
        }

        public ILeaderboard GetBoard(BoardId boardId, SortDirection sort)
        {
            lock (_lock)
            {
                if (!_handles.TryGetValue(boardId, out var handle))
                {
                    handle = new LeaderboardHandle(boardId, sort, this);
                    _handles[boardId] = handle;
                }
                return handle;
            }
        }

        public async Task FlushAsync()
        {
            if (_disposed) return;

            Dictionary<BoardId, (long score, SortDirection sort)> pending;
            lock (_lock)
            {
                pending = new Dictionary<BoardId, (long, SortDirection)>(_batcher);
                _batcher.Clear();
            }

            if (pending.Count == 0) return;

            string displayName = GetPlayerDisplayName();
            string uid = await GetUidAsync();

            Debug.Log($"[Leaderboard] Service: Flush — {pending.Count} pending scores, displayName={displayName}, uid={uid}");

            foreach (var kvp in pending)
            {
                BoardId boardId = kvp.Key;
                long score = kvp.Value.score;
                SortDirection sort = kvp.Value.sort;

                try
                {
                    var fullBoardId = new BoardId(_prefix + boardId.Value);
                    bool accepted = await _provider.SubmitScoreAsync(fullBoardId, uid, score, displayName, sort);
                    Debug.Log($"[Leaderboard] Service: Submitted board={fullBoardId.Value} score={score} → {(accepted ? "accepted" : "rejected")}");
                    if (accepted)
                    {
                        // The caches are keyed by the board id WITHOUT the prefix
                        // (see GetTopInternalAsync). Invalidating by fullBoardId
                        // missed them whenever the prefix was set (Editor and
                        // development builds), and the board kept showing the
                        // pre-submit result until the scene reloaded.
                        InvalidateCache(boardId);

                        var rank = await _provider.GetPlayerRankAsync(fullBoardId, sort, uid);
                        if (rank.Rank.HasValue && rank.Rank.Value <= TopRankThreshold)
                        {
                            OnTopRankAchieved?.Invoke(boardId, rank.Rank.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"LeaderboardService: flush failed for {boardId}: {ex.Message}");
                    lock (_lock) { _batcher[boardId] = (score, sort); }
                }
            }
        }

        public Task<string> GetPlayerUidAsync() => GetUidAsync();

        public string GetPlayerDisplayName()
        {
            string displayName = _displayNameResolver();
            return string.IsNullOrEmpty(displayName) ? "Player" : displayName;
        }

        public void Dispose()
        {
            _disposed = true;
            _provider?.Dispose();
        }

        private async Task<string> GetUidAsync()
        {
            if (_cachedUid != null) return _cachedUid;

            try
            {
                var authResult = await _auth.LoginAsync();
                if (authResult is AuthResult.Success success && success.Uid.IsValid)
                {
                    _cachedUid = success.Uid.Value;
                    return _cachedUid;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Leaderboard] Service: auth failed — {ex.Message}, falling back to PlayerPrefs GUID");
            }

            const string key = "longleap_device_id";
            if (!PlayerPrefs.HasKey(key))
                PlayerPrefs.SetString(key, System.Guid.NewGuid().ToString("N"));
            _cachedUid = PlayerPrefs.GetString(key);
            return _cachedUid;
        }

        internal async Task<SubmitResult> SubmitScoreAsync(BoardId boardId, long score, SortDirection sort)
        {
            if (_disposed)
                return new SubmitResult(false, false, score);

            lock (_lock)
            {
                if (_batcher.TryGetValue(boardId, out var existing))
                {
                    if (sort == SortDirection.Descending && score <= existing.score)
                        return new SubmitResult(false, false, score);
                    if (sort == SortDirection.Ascending && score >= existing.score)
                        return new SubmitResult(false, false, score);
                }
                _batcher[boardId] = (score, sort);
            }

            return new SubmitResult(true, true, score);
        }

        internal async Task<LeaderboardPage> GetTopInternalAsync(BoardId boardId, SortDirection sort, int count)
        {
            if (_disposed) return LeaderboardPage.Empty();

            lock (_lock)
            {
                if (_topCache.TryGetValue(boardId, out var cached))
                {
                    Debug.Log($"[Leaderboard] Service: GetTop board={boardId.Value} → cache ({cached.Entries?.Count ?? 0} entries)");
                    return cached;
                }
            }

            try
            {
                var fullBoardId = new BoardId(_prefix + boardId.Value);
                var page = await _provider.GetTopAsync(fullBoardId, sort, count);
                Debug.Log($"[Leaderboard] Service: GetTop board={boardId.Value} → fetched ({page.Entries?.Count ?? 0} entries)");
                lock (_lock)
                {
                    _topCache[boardId] = page;
                }
                return page;
            }
            catch
            {
                lock (_lock)
                {
                    if (_topCache.TryGetValue(boardId, out var cached))
                        return cached;
                }
                return LeaderboardPage.Empty();
            }
        }

        internal async Task<PlayerRank> GetMyRankInternalAsync(BoardId boardId, SortDirection sort)
        {
            if (_disposed) return new PlayerRank(null, null, 0);

            string uid = await GetUidAsync();

            try
            {
                var fullBoardId = new BoardId(_prefix + boardId.Value);
                var rank = await _provider.GetPlayerRankAsync(fullBoardId, sort, uid);
                Debug.Log($"[Leaderboard] Service: GetRank board={boardId.Value} → rank={rank.Rank}/{rank.TotalPlayers}, score={rank.Score}");
                return rank;
            }
            catch
            {
                return new PlayerRank(null, null, 0);
            }
        }

        internal async Task<LeaderboardPage> GetSurroundingsInternalAsync(BoardId boardId, SortDirection sort, int radius)
        {
            if (_disposed) return LeaderboardPage.Empty();

            string uid = await GetUidAsync();

            radius = Math.Clamp(radius, 0, 25);

            lock (_lock)
            {
                if (_surroundingsCache.TryGetValue(boardId, out var cached))
                {
                    Debug.Log($"[Leaderboard] Service: GetSurroundings board={boardId.Value} → cache ({cached.Entries?.Count ?? 0} entries)");
                    return cached;
                }
            }

            try
            {
                var fullBoardId = new BoardId(_prefix + boardId.Value);
                var page = await _provider.GetSurroundingsAsync(fullBoardId, sort, uid, radius);
                Debug.Log($"[Leaderboard] Service: GetSurroundings board={boardId.Value} → fetched ({page.Entries?.Count ?? 0} entries)");
                lock (_lock)
                {
                    _surroundingsCache[boardId] = page;
                }
                return page;
            }
            catch
            {
                lock (_lock)
                {
                    if (_surroundingsCache.TryGetValue(boardId, out var cached))
                        return cached;
                }
                return LeaderboardPage.Empty();
            }
        }

        internal void InvalidateCache(BoardId boardId)
        {
            lock (_lock)
            {
                _topCache.Remove(boardId);
                _surroundingsCache.Remove(boardId);
            }
        }

        private sealed class LeaderboardHandle : ILeaderboard
        {
            private readonly BoardId _boardId;
            private readonly SortDirection _sort;
            private readonly LeaderboardService _service;

            public BoardId Id => _boardId;

            public LeaderboardHandle(BoardId boardId, SortDirection sort, LeaderboardService service)
            {
                _boardId = boardId;
                _sort = sort;
                _service = service;
            }

            public async Task<SubmitResult> SubmitScoreAsync(long score)
            {
                return await _service.SubmitScoreAsync(_boardId, score, _sort);
            }

            public async Task<LeaderboardPage> GetTopAsync(int count)
            {
                return await _service.GetTopInternalAsync(_boardId, _sort, count);
            }

            public async Task<PlayerRank> GetMyRankAsync()
            {
                return await _service.GetMyRankInternalAsync(_boardId, _sort);
            }

            public async Task<LeaderboardPage> GetSurroundingsAsync(int radius)
            {
                return await _service.GetSurroundingsInternalAsync(_boardId, _sort, radius);
            }
        }
    }
}
