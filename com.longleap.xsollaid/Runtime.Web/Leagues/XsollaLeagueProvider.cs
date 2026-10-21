using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.Leagues;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LongLeap.XsollaId.Web
{
    // Leagues of one Xsolla ID account on our server (RewardService, the
    // /leagues endpoints).
    //
    // Same rules as XsollaLeaderboardProvider: the account is fixed at
    // construction (created only for a signed-in player), the player is
    // identified by xsollaId without a token, and the server never returns
    // other players' ids — each group row carries isMe instead.
    //
    // Every method throws on a failed request or an unreadable reply;
    // LeagueService decides what to do about it.
    public sealed class XsollaLeagueProvider : ILeagueProvider
    {
        public const string DefaultLeaguesPath = "/leagues";

        private readonly string _baseUrl;
        private readonly string _gameId;
        private readonly string _leaguesPath;
        private readonly string _accountId;
        private readonly XsollaIdHttp _http;

        public XsollaLeagueProvider(string baseUrl, string gameId, string accountId, XsollaIdHttp http = null,
            string leaguesPath = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException($"{nameof(XsollaLeagueProvider)}: baseUrl is empty.", nameof(baseUrl));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException($"{nameof(XsollaLeagueProvider)}: gameId is empty.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException($"{nameof(XsollaLeagueProvider)}: accountId is empty.", nameof(accountId));

            _baseUrl = baseUrl.Trim().TrimEnd('/');
            _gameId = gameId.Trim();
            _leaguesPath = NormalizePath(leaguesPath);
            _accountId = accountId;
            _http = http ?? new XsollaIdHttp();
        }

        public async Task<LeagueState> GetStateAsync(string leagueId)
        {
            string url = LeagueUrl(leagueId) +
                $"?gameId={Uri.EscapeDataString(_gameId)}" +
                $"&xsollaId={Uri.EscapeDataString(_accountId)}";

            HttpReply reply = await _http.GetAsync(url);
            EnsureSuccess(reply, "GET", url);

            return ParseState(Parse(reply, "GET", url));
        }

        public async Task<LeagueState> SubmitScoreAsync(string leagueId, long score, string displayName)
        {
            var body = PlayerBody();
            body["score"] = score;
            body["displayName"] = displayName;

            JObject json = await PostAsync(LeagueUrl(leagueId) + "/scores", body);
            return ParseState(json);
        }

        public async Task<LeagueClaim> ClaimAsync(string leagueId, int periodNumber)
        {
            JObject json = await PostAsync(
                LeagueUrl(leagueId) + $"/results/{periodNumber}/claim", PlayerBody());

            return new LeagueClaim(
                (bool?)json["claimed"] ?? false,
                (int?)json["reward"] ?? 0);
        }

        public async Task<LeagueState> DebugFillGroupAsync(string leagueId)
        {
            string url = LeagueUrl(leagueId) + "/debug/fill";

            HttpReply reply = await _http.RequestAsync(new HttpCall(
                "POST", url, PlayerBody().ToString(Formatting.None), "application/json"));

            // 409 not_joined: the player has no group this period.
            if (reply.StatusCode == 409)
                return null;

            EnsureSuccess(reply, "POST", url);

            return ParseState(Parse(reply, "POST", url)["state"] as JObject);
        }

        public async Task DebugClosePeriodAsync(string leagueId)
        {
            await PostAsync(LeagueUrl(leagueId) + "/debug/close", PlayerBody());
        }

        public void Dispose()
        {
            // Nothing to release: the transport holds no connections.
        }

        // ------------------------------------------------------------------

        private JObject PlayerBody() => new JObject
        {
            ["gameId"] = _gameId,
            ["xsollaId"] = _accountId
        };

        private async Task<JObject> PostAsync(string url, JObject body)
        {
            HttpReply reply = await _http.RequestAsync(new HttpCall(
                "POST", url, body.ToString(Formatting.None), "application/json"));

            EnsureSuccess(reply, "POST", url);
            return Parse(reply, "POST", url);
        }

        private static LeagueState ParseState(JObject json)
        {
            if (json == null)
                throw new InvalidOperationException($"{nameof(XsollaLeagueProvider)}: the reply has no league state.");

            var tiers = new List<string>();

            if (json["tiers"] is JArray tierArray)
            {
                foreach (JToken tier in tierArray)
                    tiers.Add((string)tier ?? string.Empty);
            }

            var periodJson = json["period"] as JObject;

            var period = new LeaguePeriod(
                (int?)periodJson?["number"] ?? 0,
                FromUnixMs((long?)periodJson?["startsAt"]),
                FromUnixMs((long?)periodJson?["endsAt"]));

            var unclaimed = new List<LeagueResult>();

            if (json["unclaimed"] is JArray unclaimedArray)
            {
                foreach (JToken result in unclaimedArray)
                {
                    if (result is JObject resultJson)
                        unclaimed.Add(ParseResult(resultJson));
                }
            }

            return new LeagueState(
                (string)json["leagueId"],
                tiers,
                period,
                (int?)json["tier"] ?? 0,
                json["group"] is JObject group ? ParseGroup(group) : null,
                json["lastResult"] is JObject last ? ParseResult(last) : null,
                unclaimed);
        }

        private static LeagueGroup ParseGroup(JObject json)
        {
            var entries = new List<LeagueEntry>();

            if (json["entries"] is JArray rows)
            {
                foreach (JToken row in rows)
                {
                    entries.Add(new LeagueEntry(
                        (int?)row["place"] ?? entries.Count + 1,
                        (string)row["displayName"],
                        (long?)row["score"] ?? 0,
                        (bool?)row["isMe"] ?? false,
                        (int?)row["reward"] ?? 0));
                }
            }

            return new LeagueGroup(
                (int?)json["number"] ?? 0,
                (int?)json["promotePlaces"] ?? 0,
                (int?)json["demoteFromPlace"] ?? 0,
                (int?)json["place"] ?? 0,
                (long?)json["score"] ?? 0,
                (long?)json["scoreToPromotion"] ?? 0,
                entries);
        }

        private static LeagueResult ParseResult(JObject json)
        {
            return new LeagueResult(
                (int?)json["periodNumber"] ?? 0,
                (int?)json["place"] ?? 0,
                (long?)json["score"] ?? 0,
                (int?)json["tier"] ?? 0,
                (int?)json["newTier"] ?? 0,
                ParseOutcome((string)json["outcome"]),
                (int?)json["reward"] ?? 0,
                (bool?)json["claimed"] ?? false);
        }

        private static LeagueOutcome ParseOutcome(string value)
        {
            switch (value)
            {
                case "promoted": return LeagueOutcome.Promoted;
                case "demoted": return LeagueOutcome.Demoted;
                default: return LeagueOutcome.Stayed;
            }
        }

        private static DateTimeOffset FromUnixMs(long? value) =>
            DateTimeOffset.FromUnixTimeMilliseconds(value ?? 0);

        private string LeagueUrl(string leagueId) =>
            $"{_baseUrl}{_leaguesPath}/{Uri.EscapeDataString(leagueId)}";

        private static void EnsureSuccess(HttpReply reply, string method, string url)
        {
            if (!reply.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"{nameof(XsollaLeagueProvider)}: request failed — {Describe(reply, method, url)}");
            }
        }

        private static JObject Parse(HttpReply reply, string method, string url)
        {
            try
            {
                return JObject.Parse(reply.Body);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{nameof(XsollaLeagueProvider)}: invalid JSON from {method} {url}: {ex.Message}");
            }
        }

        // The query only carries gameId and xsollaId, no tokens, so the URL is
        // safe to log.
        private static string Describe(HttpReply reply, string method, string url)
        {
            string request = $"{method} {url}";

            if (reply.IsTransportError)
                return $"{reply.TransportError} ({request})";

            string body = string.IsNullOrEmpty(reply.Body) ? "<empty body>" : Truncate(reply.Body);
            string hint = reply.StatusCode == 404 && string.IsNullOrEmpty(reply.Body)
                ? " — the route is likely not deployed on the server (check baseUrl and that the /leagues endpoints are live)"
                : string.Empty;

            return $"HTTP {reply.StatusCode} {body} ({request}){hint}";
        }

        private static string NormalizePath(string value)
        {
            string path = value?.Trim().TrimEnd('/');

            if (string.IsNullOrEmpty(path))
                return DefaultLeaguesPath;

            return path[0] == '/' ? path : "/" + path;
        }

        private static string Truncate(string body) =>
            string.IsNullOrEmpty(body) || body.Length <= 200 ? body : body.Substring(0, 200) + "…";
    }
}
