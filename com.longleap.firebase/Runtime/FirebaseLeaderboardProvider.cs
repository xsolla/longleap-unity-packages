using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.Core.Leaderboards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.Firebase
{
    /// <summary>
    /// Leaderboards stored in Firebase Realtime Database over REST.
    /// </summary>
    /// <remarks>
    /// Entries live at <c>leaderboards/{boardId}/{uid}</c> with <c>score</c>, <c>negScore</c>,
    /// <c>displayName</c> and a server <c>timestamp</c>. Queries order by <c>negScore</c>, so
    /// the database rules must declare <c>".indexOn": "negScore"</c> for each board.
    /// A submit is written only if it beats the player's stored score. After
    /// <see cref="Dispose"/> every call returns an empty or failed result.
    /// </remarks>
    public sealed class FirebaseLeaderboardProvider : ILeaderboardProvider
    {
        private readonly FirebaseHttpClient _http;
        private bool _disposed;

        /// <param name="http">RTDB client used for all reads and writes.</param>
        public FirebaseLeaderboardProvider(FirebaseHttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<bool> SubmitScoreAsync(BoardId boardId, string uid, long score, string displayName, SortDirection sort)
        {
            if (_disposed) return false;

            try
            {
                double? existingScore = await ReadPlayerScoreAsync(boardId, uid);
                if (existingScore.HasValue)
                {
                    if (sort == SortDirection.Descending && score <= existingScore.Value)
                        return false;
                    if (sort == SortDirection.Ascending && score >= existingScore.Value)
                        return false;
                }

                var multiLocationData = new JObject
                {
                    [$"leaderboards/{boardId.Value}/{uid}/score"] = score,
                    [$"leaderboards/{boardId.Value}/{uid}/negScore"] = sort == SortDirection.Descending ? -score : score,
                    [$"leaderboards/{boardId.Value}/{uid}/displayName"] = displayName,
                    [$"leaderboards/{boardId.Value}/{uid}/timestamp"] = new JObject { [".sv"] = "timestamp" }
                };

                string body = JsonConvert.SerializeObject(multiLocationData);
                var response = await _http.PatchAsync("", body);

                if (response == null) return false;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseLeaderboardProvider] Submit board={boardId.Value} FAILED — {ex.Message}");
                return false;
            }
        }

        public async Task<LeaderboardPage> GetTopAsync(BoardId boardId, SortDirection sort, int count)
        {
            if (_disposed) return LeaderboardPage.Empty();

            try
            {
                string orderBy = "negScore";
                int limit = count;
                string query = $"&orderBy=\"{orderBy}\"&limitToFirst={limit}";

                string response = await _http.GetAsync($"leaderboards/{boardId.Value}", query);
                if (response == null || response == "null")
                    return LeaderboardPage.Empty();

                var dict = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(response);
                if (dict == null || dict.Count == 0)
                    return LeaderboardPage.Empty();

                var entries = new List<LeaderboardEntry>();
                foreach (var kvp in dict)
                {
                    string entryUid = kvp.Key;
                    var data = kvp.Value;
                    long entryScore = data["score"]?.Value<long>() ?? 0;
                    string displayName = data["displayName"]?.Value<string>() ?? "";
                    long timestamp = data["timestamp"]?.Value<long>() ?? 0;
                    entries.Add(new LeaderboardEntry(entryUid, displayName, entryScore, 0, timestamp));
                }

                if (sort == SortDirection.Descending)
                    entries = entries.OrderByDescending(e => e.Score).ToList();
                else
                    entries = entries.OrderBy(e => e.Score).ToList();

                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    entries[i] = new LeaderboardEntry(e.Uid, e.DisplayName, e.Score, i + 1, e.Timestamp);
                }

                return new LeaderboardPage(entries.AsReadOnly(), null, false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FirebaseLeaderboardProvider] GetTop FAILED — {ex.Message}");
                return LeaderboardPage.Empty();
            }
        }

        public async Task<PlayerRank> GetPlayerRankAsync(BoardId boardId, SortDirection sort, string uid)
        {
            if (_disposed) return new PlayerRank(null, null, 0);

            try
            {
                double? rawScore = await ReadPlayerScoreAsync(boardId, uid);
                if (!rawScore.HasValue)
                    return new PlayerRank(null, null, 0);

                long playerScore = (long)rawScore.Value;
                long playerNegScore = sort == SortDirection.Descending ? -playerScore : playerScore;

                string rankResp = await _http.GetAsync($"leaderboards/{boardId.Value}", $"&orderBy=\"negScore\"&endAt={playerNegScore - 1}");
                string shallowResp = await _http.GetAsync($"leaderboards/{boardId.Value}", "&shallow=true");

                int total = CountKeys(shallowResp);
                if (total == 0) return new PlayerRank(null, null, 0);

                int numAbove = CountKeys(rankResp);
                return new PlayerRank(numAbove + 1, playerScore, total);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseLeaderboardProvider] GetRank FAILED — {ex.Message}");
                return new PlayerRank(null, null, 0);
            }
        }

        public async Task<LeaderboardPage> GetSurroundingsAsync(BoardId boardId, SortDirection sort, string uid, int radius)
        {
            if (_disposed) return LeaderboardPage.Empty();

            radius = Math.Clamp(radius, 0, 25);

            try
            {
                double? rawScore = await ReadPlayerScoreAsync(boardId, uid);
                if (!rawScore.HasValue)
                    return LeaderboardPage.Empty();

                long playerScore = (long)rawScore.Value;
                long playerNegScore = sort == SortDirection.Descending ? -playerScore : playerScore;

                string aboveResp = await _http.GetAsync($"leaderboards/{boardId.Value}", $"&orderBy=\"negScore\"&endAt={playerNegScore - 1}&limitToLast={radius}");
                string belowResp = await _http.GetAsync($"leaderboards/{boardId.Value}", $"&orderBy=\"negScore\"&startAt={playerNegScore}&limitToFirst={radius + 1}");

                var aboveDict = ParseBoardResponse(aboveResp);
                var belowDict = ParseBoardResponse(belowResp);

                var aboveList = SortEntries(aboveDict, sort);
                var belowList = SortEntries(belowDict, sort);

                int playerIdx = belowList.FindIndex(e => e.uid == uid);
                if (playerIdx < 0) return LeaderboardPage.Empty();

                var playerEntry = belowList[playerIdx];
                belowList.RemoveAt(playerIdx);

                int numAbove = aboveList.Count < radius ? aboveList.Count : CountKeys(aboveResp);

                var result = new List<LeaderboardEntry>();
                int rank = numAbove - aboveList.Count + 1;
                foreach (var e in aboveList)
                    result.Add(new LeaderboardEntry(e.uid, e.displayName, e.score, rank++, e.timestamp));

                result.Add(new LeaderboardEntry(playerEntry.uid, playerEntry.displayName, playerEntry.score, numAbove + 1, playerEntry.timestamp));

                rank = numAbove + 2;
                foreach (var e in belowList)
                    result.Add(new LeaderboardEntry(e.uid, e.displayName, e.score, rank++, e.timestamp));

                return new LeaderboardPage(result.AsReadOnly(), null, false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseLeaderboardProvider] GetSurroundings FAILED — {ex.Message}");
                return LeaderboardPage.Empty();
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private async Task<double?> ReadPlayerScoreAsync(BoardId boardId, string uid)
        {
            try
            {
                string response = await _http.GetAsync($"leaderboards/{boardId.Value}/{uid}/score");
                if (response == null || response == "null") return null;
                if (double.TryParse(response, out double score)) return score;
                return null;
            }
            catch { return null; }
        }

        private static int CountKeys(string response)
        {
            if (response == null || response == "null" || response == "{}") return 0;
            try { return JObject.Parse(response).Count; } catch { return 0; }
        }

        private static Dictionary<string, JObject> ParseBoardResponse(string response)
        {
            if (response == null || response == "null") return new Dictionary<string, JObject>();
            return JsonConvert.DeserializeObject<Dictionary<string, JObject>>(response) ?? new Dictionary<string, JObject>();
        }

        private static List<(string uid, string displayName, long score, long timestamp)> SortEntries(
            Dictionary<string, JObject> dict, SortDirection sort)
        {
            var entries = new List<(string uid, string displayName, long score, long timestamp)>();
            foreach (var kvp in dict)
            {
                long s = kvp.Value["score"]?.Value<long>() ?? 0;
                long t = kvp.Value["timestamp"]?.Value<long>() ?? 0;
                string name = kvp.Value["displayName"]?.Value<string>() ?? "";
                entries.Add((kvp.Key, name, s, t));
            }

            if (sort == SortDirection.Descending)
                entries = entries.OrderByDescending(e => e.score).ToList();
            else
                entries = entries.OrderBy(e => e.score).ToList();

            return entries;
        }
    }
}
