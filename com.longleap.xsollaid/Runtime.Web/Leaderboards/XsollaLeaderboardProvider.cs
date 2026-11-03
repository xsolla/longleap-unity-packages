using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.Leaderboards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Leaderboards of one Xsolla ID account on our server (RewardService, the
    // /leaderboards endpoints).
    //
    // Like XsollaCloudSaveProvider, the account is set at construction and
    // never changes: the provider is created only for a signed-in player, and
    // an account change always goes through a scene reload. Guests get no
    // leaderboard at all.
    //
    // The server never returns other players' xsollaId (there is no auth, so
    // an id would open that player's saves to anyone). Each row carries isMe
    // instead; the player's own row gets the account id as uid, every other
    // row an opaque "rank:N", which is enough for the leaderboard UI to find
    // "you" in the list.
    //
    // The player is identified by xsollaId (sub) without a token — same as
    // rewards and cloud saves. The uid argument of the interface methods is
    // ignored: it is always this account (see XsollaAccountAuthProvider).
    public sealed class XsollaLeaderboardProvider : ILeaderboardProvider
    {
        // Default for when the caller passes no path; whoever wires the provider
        // passes the configured one.
        public const string DefaultLeaderboardsPath = "/leaderboards";

        private readonly string _baseUrl;
        private readonly string _gameId;
        private readonly string _leaderboardsPath;
        private readonly string _accountId;
        private readonly XsollaIdHttp _http;

        public XsollaLeaderboardProvider(string baseUrl, string gameId, string accountId, XsollaIdHttp http = null,
            string leaderboardsPath = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException($"{nameof(XsollaLeaderboardProvider)}: baseUrl is empty.", nameof(baseUrl));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException($"{nameof(XsollaLeaderboardProvider)}: gameId is empty.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException($"{nameof(XsollaLeaderboardProvider)}: accountId is empty.", nameof(accountId));

            _baseUrl = baseUrl.Trim().TrimEnd('/');
            _gameId = gameId.Trim();
            _leaderboardsPath = NormalizePath(leaderboardsPath);
            _accountId = accountId;
            _http = http ?? new XsollaIdHttp();
        }

        public string AccountId => _accountId;

        // Throws on failure: LeaderboardService.FlushAsync catches it and keeps
        // the score queued for the next flush. false means the server is fine
        // but the score did not beat the stored best.
        public async Task<bool> SubmitScoreAsync(BoardId boardId, string uid, long score, string displayName,
            SortDirection sort)
        {
            var body = new JObject
            {
                ["gameId"] = _gameId,
                ["xsollaId"] = _accountId,
                ["score"] = score,
                ["displayName"] = displayName
            };

            string url = BoardUrl(boardId) + "/scores";
            HttpReply reply = await _http.RequestAsync(new HttpCall(
                "POST", url, body.ToString(Formatting.None), "application/json"));

            if (!reply.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"{nameof(XsollaLeaderboardProvider)}: SubmitScoreAsync '{boardId.Value}' failed — " +
                    Describe(reply, "POST", url));
            }

            try
            {
                return (bool?)JObject.Parse(reply.Body)["accepted"] ?? false;
            }
            catch (Exception ex)
            {
                // The score did reach the server; resending it would change
                // nothing, so this is not worth a retry.
                Debug.LogWarning($"{nameof(XsollaLeaderboardProvider)}: failed to parse the submit reply " +
                    $"for '{boardId.Value}': {ex.Message}");
                return false;
            }
        }

        // Never throws: an empty page on a network failure, so the screen shows
        // an empty board instead of breaking.
        public async Task<LeaderboardPage> GetTopAsync(BoardId boardId, SortDirection sort, int count)
        {
            JObject json = await TryReadBoardAsync(boardId, Math.Max(0, count), "GetTopAsync");

            if (json == null)
                return LeaderboardPage.Empty();

            try
            {
                var entries = new List<LeaderboardEntry>();

                if (json["entries"] is JArray rows)
                {
                    foreach (JToken row in rows)
                    {
                        int rank = (int?)row["rank"] ?? entries.Count + 1;
                        bool isMe = (bool?)row["isMe"] ?? false;

                        entries.Add(new LeaderboardEntry(
                            isMe ? _accountId : $"rank:{rank}",
                            (string)row["displayName"],
                            (long?)row["score"] ?? 0,
                            rank,
                            (long?)row["updatedAt"] ?? 0));
                    }
                }

                int total = (int?)json["total"] ?? entries.Count;

                return new LeaderboardPage(entries, null, total > entries.Count);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaLeaderboardProvider)}: failed to parse the top of " +
                    $"'{boardId.Value}': {ex.Message}");
                return LeaderboardPage.Empty();
            }
        }

        // Never throws. Rank and Score are null while the player has no score
        // on the board.
        public async Task<PlayerRank> GetPlayerRankAsync(BoardId boardId, SortDirection sort, string uid)
        {
            // limit=0: only the player's own position, no top rows.
            JObject json = await TryReadBoardAsync(boardId, 0, "GetPlayerRankAsync");

            if (json == null)
                return new PlayerRank(null, null, 0);

            try
            {
                int total = (int?)json["total"] ?? 0;

                if (!(json["me"] is JObject me))
                    return new PlayerRank(null, null, total);

                return new PlayerRank((int?)me["rank"], (long?)me["score"], total);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaLeaderboardProvider)}: failed to parse the rank on " +
                    $"'{boardId.Value}': {ex.Message}");
                return new PlayerRank(null, null, 0);
            }
        }

        // Not supported by the server: an empty page. The leaderboard UI then
        // builds the player's own row from GetPlayerRankAsync.
        public Task<LeaderboardPage> GetSurroundingsAsync(BoardId boardId, SortDirection sort, string uid, int radius) =>
            Task.FromResult(LeaderboardPage.Empty());

        public void Dispose()
        {
            // Nothing to release: the transport holds no connections.
        }

        // The board reply as JSON, or null if it could not be read.
        private async Task<JObject> TryReadBoardAsync(BoardId boardId, int limit, string what)
        {
            string url = BoardUrl(boardId) +
                $"?gameId={Uri.EscapeDataString(_gameId)}" +
                $"&xsollaId={Uri.EscapeDataString(_accountId)}" +
                $"&limit={limit}";

            HttpReply reply = await _http.GetAsync(url);

            if (!reply.IsSuccess)
            {
                Debug.LogWarning($"{nameof(XsollaLeaderboardProvider)}: {what} '{boardId.Value}' failed — " +
                    Describe(reply, "GET", url));
                return null;
            }

            try
            {
                return JObject.Parse(reply.Body);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaLeaderboardProvider)}: {what} '{boardId.Value}' returned " +
                    $"invalid JSON: {ex.Message}");
                return null;
            }
        }

        private string BoardUrl(BoardId boardId) =>
            $"{_baseUrl}{_leaderboardsPath}/{Uri.EscapeDataString(boardId.Value)}";

        // Method and URL go into the message, as in XsollaCloudSaveProvider:
        // a bare "HTTP 404" does not say which endpoint is missing. The query
        // only carries gameId, xsollaId and limit, no tokens, so the URL is
        // safe to log.
        private static string Describe(HttpReply reply, string method, string url)
        {
            string request = $"{method} {url}";

            if (reply.IsTransportError)
                return $"{reply.TransportError} ({request})";

            string body = string.IsNullOrEmpty(reply.Body) ? "<empty body>" : Truncate(reply.Body);
            string hint = reply.StatusCode == 404 && string.IsNullOrEmpty(reply.Body)
                ? " — the route is likely not deployed on the server (check baseUrl and that the /leaderboards endpoints are live)"
                : string.Empty;

            return $"HTTP {reply.StatusCode} {body} ({request}){hint}";
        }

        // Same rule as BackPackEndpoint: empty means the default, a missing
        // leading slash is added — either would otherwise send the request to
        // the wrong route.
        private static string NormalizePath(string value)
        {
            string path = value?.Trim().TrimEnd('/');

            if (string.IsNullOrEmpty(path))
                return DefaultLeaderboardsPath;

            return path[0] == '/' ? path : "/" + path;
        }

        private static string Truncate(string body) =>
            string.IsNullOrEmpty(body) || body.Length <= 200 ? body : body.Substring(0, 200) + "…";
    }
}
