using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LongLeap.Quests
{
    // IQuestService over the Quests service routes. Its own address (QuestConfigSO)
    // and its own transport: it shares nothing with BackPack.
    public sealed class QuestService : IQuestService
    {
        public const string EventsRoute = "/v1/users/{0}/quests/events";
        public const string ListRoute = "/v1/quests";
        public const string PlayerListRoute = "/v1/users/{0}/quests";
        public const string LoginTokenHeader = "X-Player-Login-Token";

        private const string Timestamp = "yyyy-MM-ddTHH:mm:ssZ";

        private readonly Func<string> _xsollaIdProvider;
        private readonly QuestEndpoint _endpoint;
        private readonly IQuestTransport _transport;

        // endpoint == null or not configured — the service is off and answers
        // "not_configured". transport == null means UnityWebRequestQuestTransport.
        public QuestService(Func<string> xsollaIdProvider, QuestEndpoint endpoint, IQuestTransport transport = null)
        {
            _xsollaIdProvider = xsollaIdProvider;
            _endpoint = endpoint;
            _transport = transport ?? new UnityWebRequestQuestTransport();
        }

        public bool IsAvailable => _endpoint != null && _endpoint.IsConfigured;

        public bool HasXsollaId => !string.IsNullOrEmpty(XsollaId);

        private string XsollaId => _xsollaIdProvider?.Invoke();

        public async Task<QuestReply> SendEventAsync(
            string eventName,
            IReadOnlyDictionary<string, string> properties = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(eventName))
                return QuestReply.Failed("empty_event_name");

            if (!IsAvailable)
                return QuestReply.Failed("not_configured");

            string xsollaId = XsollaId;

            if (string.IsNullOrEmpty(xsollaId))
                return QuestReply.Failed("no_xsolla_id");

            var props = new Dictionary<string, string>();
            if (properties != null)
            {
                foreach (var pair in properties)
                    props[pair.Key] = pair.Value;
            }

            string body = JsonConvert.SerializeObject(new QuestEventBody
            {
                Name = eventName,
                IdempotencyKey = StableEventId(_endpoint.GameId, xsollaId, eventName),
                ClientTimestamp = DateTime.UtcNow.ToString(Timestamp, CultureInfo.InvariantCulture),
                Properties = props
            });

            QuestHttpReply raw = await Send("POST", Url(EventsRoute, xsollaId), body, null, cancellationToken);

            var envelope = ParseEnvelope<QuestEventAccepted>(raw, out string failure, out bool retryable);

            return envelope != null ? QuestReply.Ok() : QuestReply.Failed(failure, retryable);
        }

        public async Task<QuestPageReply> GetQuestsAsync(
            int page = 1, int limit = 10, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
                return QuestPageReply.Failed("not_configured");

            QuestHttpReply raw = await Send("GET", PageUrl(ListRoute, null, page, limit), null, null, cancellationToken);

            return ReadPage(raw);
        }

        public async Task<QuestPageReply> GetPlayerQuestsAsync(
            string loginToken, int page = 1, int limit = 10, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
                return QuestPageReply.Failed("not_configured");

            if (string.IsNullOrEmpty(loginToken))
                return QuestPageReply.Failed("no_login_token");

            string xsollaId = XsollaId;

            if (string.IsNullOrEmpty(xsollaId))
                return QuestPageReply.Failed("no_xsolla_id");

            var headers = new Dictionary<string, string> { [LoginTokenHeader] = loginToken };

            QuestHttpReply raw = await Send(
                "GET", PageUrl(PlayerListRoute, xsollaId, page, limit), null, headers, cancellationToken);

            return ReadPage(raw);
        }

        // The idempotency key is DETERMINISTIC: the same event for the same player is
        // the same key, so a resend is dropped by the server as a duplicate. Shaped as
        // a version 3 (name-based) UUID — the service requires a UUID.
        public static string StableEventId(string gameId, string xsollaId, string eventName)
        {
            byte[] payload = Encoding.UTF8.GetBytes($"quests|{gameId}|{xsollaId}|{eventName}");
            var bytes = new byte[16];

            BitConverter.GetBytes(Fnv1a64(payload, 0xcbf29ce484222325UL)).CopyTo(bytes, 0);
            BitConverter.GetBytes(Fnv1a64(payload, 0x9e3779b97f4a7c15UL)).CopyTo(bytes, 8);

            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            return new Guid(bytes).ToString();
        }

        private static ulong Fnv1a64(byte[] data, ulong seed)
        {
            ulong hash = seed;

            foreach (byte b in data)
            {
                hash ^= b;
                hash *= 0x100000001b3UL;
            }

            return hash;
        }

        // No exceptions escape except the caller's own cancellation.
        private async Task<QuestHttpReply> Send(
            string method, string url, string body,
            IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            try
            {
                return await _transport.SendAsync(method, url, body, headers, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                return new QuestHttpReply(0, null, $"{method} {url} — {e.Message}");
            }
        }

        private string Url(string template, string xsollaId) => _endpoint.BaseUrl + Route(template, xsollaId);

        private string PageUrl(string template, string xsollaId, int page, int limit)
        {
            string path = xsollaId == null ? template : Route(template, xsollaId);
            return $"{_endpoint.BaseUrl}{path}?page={page}&limit={limit}";
        }

        private static string Route(string template, string xsollaId) =>
            string.Format(CultureInfo.InvariantCulture, template, Uri.EscapeDataString(xsollaId));

        // The envelope, or null with the reason. Success needs both a 2xx and is_success.
        private static QuestEnvelope<T> ParseEnvelope<T>(QuestHttpReply raw, out string failure, out bool retryable)
        {
            failure = null;
            retryable = false;

            if (raw.IsTransportError)
            {
                failure = $"transport: {raw.TransportError}";
                retryable = true;
                return null;
            }

            if (string.IsNullOrWhiteSpace(raw.Body))
            {
                failure = $"http_{raw.StatusCode}_empty_body";
                return null;
            }

            QuestEnvelope<T> envelope;

            try
            {
                envelope = JsonConvert.DeserializeObject<QuestEnvelope<T>>(raw.Body);
            }
            catch (Exception e)
            {
                failure = $"bad_json: {e.Message}";
                return null;
            }

            if (envelope == null)
            {
                failure = $"http_{raw.StatusCode}_bad_json";
                return null;
            }

            if (!envelope.IsSuccess || !raw.IsHttpSuccess)
            {
                failure = envelope.ErrorMessageId ?? envelope.ErrorMessage ?? $"http_{raw.StatusCode}_rejected";
                retryable = envelope.Retryable;
                return null;
            }

            return envelope;
        }

        private static QuestPageReply ReadPage(QuestHttpReply raw)
        {
            var envelope = ParseEnvelope<QuestPageBody>(raw, out string failure, out _);

            if (envelope == null)
                return QuestPageReply.Failed(failure);

            var body = envelope.Payload;

            if (body == null)
                return QuestPageReply.Failed($"http_{raw.StatusCode}_no_payload");

            var quests = new List<QuestItem>();

            if (body.Data != null)
            {
                foreach (var token in body.Data)
                {
                    if (token is JObject obj)
                        quests.Add(new QuestItem { Id = obj.Value<string>("id"), Raw = obj });
                }
            }

            return QuestPageReply.Ok(body.Page, body.Limit, body.Total, quests);
        }
    }
}
