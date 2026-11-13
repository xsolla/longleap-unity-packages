using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LongLeap.Quests
{
    // Whether the service accepted an event.
    public readonly struct QuestReply
    {
        public bool Success { get; }
        public string Error { get; }

        // The service's own hint: sending again with the same key may succeed.
        public bool Retryable { get; }

        private QuestReply(bool success, string error, bool retryable)
        {
            Success = success;
            Error = error;
            Retryable = retryable;
        }

        public static QuestReply Ok() => new QuestReply(true, null, false);

        public static QuestReply Failed(string error, bool retryable = false) =>
            new QuestReply(false, error, retryable);
    }

    // A page of quests: the project's public ones (GET /v1/quests) or the player's
    // (GET /v1/users/{id}/quests), each with its progress.
    public sealed class QuestPageReply
    {
        public bool Success { get; }
        public string Error { get; }
        public int Page { get; }
        public int Limit { get; }
        public int Total { get; }
        public IReadOnlyList<QuestItem> Quests { get; }

        private QuestPageReply(bool success, string error, int page, int limit, int total, IReadOnlyList<QuestItem> quests)
        {
            Success = success;
            Error = error;
            Page = page;
            Limit = limit;
            Total = total;
            Quests = quests;
        }

        public static QuestPageReply Ok(int page, int limit, int total, IReadOnlyList<QuestItem> quests) =>
            new QuestPageReply(true, null, page, limit, total, quests);

        public static QuestPageReply Failed(string error) =>
            new QuestPageReply(false, error, 0, 0, 0, new List<QuestItem>());
    }

    // One quest of a page. Raw is the object as the service returned it (view, progress, ...).
    public sealed class QuestItem
    {
        public string Id { get; set; }

        public JObject Raw { get; set; }
    }

    // The service's response envelope.
    internal sealed class QuestEnvelope<T>
    {
        [JsonProperty("is_success")] public bool IsSuccess;
        [JsonProperty("status_code")] public int StatusCode;
        [JsonProperty("retryable")] public bool Retryable;
        [JsonProperty("error_message")] public string ErrorMessage;
        [JsonProperty("error_message_id")] public string ErrorMessageId;
        [JsonProperty("payload")] public T Payload;
    }

    // The body of POST /v1/users/{xsolla_id}/quests/events.
    internal sealed class QuestEventBody
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("idempotency_key")] public string IdempotencyKey;
        [JsonProperty("client_timestamp")] public string ClientTimestamp;
        [JsonProperty("properties")] public Dictionary<string, string> Properties;
    }

    // The payload of an accepted event: accepted, not yet processed.
    internal sealed class QuestEventAccepted
    {
        [JsonProperty("event_id")] public string EventId;
        [JsonProperty("idempotency_key")] public string IdempotencyKey;
    }

    // The payload of a page.
    internal sealed class QuestPageBody
    {
        [JsonProperty("page")] public int Page;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("total")] public int Total;
        [JsonProperty("data")] public JArray Data;
    }
}
