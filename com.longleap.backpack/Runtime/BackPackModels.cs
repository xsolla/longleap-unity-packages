using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LongLeap.Backpack
{
    // What goes to the server. A separate DTO rather than string concatenation:
    // the field is called "event" — a C# keyword — and [JsonProperty] is the only
    // way to get that name without risking manual escaping of values (the
    // achievement id and xsollaID come from outside).
    //
    // Field names are the server contract (RewardRequest: gameId, xsollaId, event,
    // eventId), so they must not be renamed here, even if the game calls the value
    // something else.
    public sealed class BackPackEventBody
    {
        [JsonProperty("gameId")] public string GameId;
        [JsonProperty("xsollaId")] public string XsollaId;
        [JsonProperty("event")] public string Event;
        [JsonProperty("eventId")] public string EventId;
    }

    // A parsed BackPack server reply.
    //
    // Success is the only thing that decides "save the achievement or not".
    // RewardGranted is separate from Success on purpose: "this event has no
    // reward" is a SUCCESSFUL answer (success: true, rewardGranted: false), and
    // the achievement must be saved on it.
    public readonly struct BackPackReply
    {
        public bool Success { get; }
        public bool RewardGranted { get; }
        public string RewardId { get; }

        // The server's error code ("invalid_client") or a description of a failure
        // that never reached the server. For the log, not for the player.
        public string Error { get; }

        private BackPackReply(bool success, bool rewardGranted, string rewardId, string error)
        {
            Success = success;
            RewardGranted = rewardGranted;
            RewardId = rewardId;
            Error = error;
        }

        public static BackPackReply Ok(bool rewardGranted, string rewardId) =>
            new BackPackReply(true, rewardGranted, rewardId, null);

        public static BackPackReply Failed(string error) =>
            new BackPackReply(false, false, null, error ?? "unknown_error");

        public override string ToString() => Success
            ? $"BackPackReply(ok, rewardGranted={RewardGranted}, rewardId={RewardId ?? "none"})"
            : $"BackPackReply(failed, error={Error})";
    }

    // The answer to "is the BackPack server reachable" (see CheckConnectionAsync).
    // Error is for the log: "not_configured", "transport: …", "http_404: …", "http_502".
    public readonly struct BackPackConnectionResult
    {
        public bool IsConnected { get; }
        public string Error { get; }

        private BackPackConnectionResult(bool isConnected, string error)
        {
            IsConnected = isConnected;
            Error = error;
        }

        public static BackPackConnectionResult Connected() => new BackPackConnectionResult(true, null);

        public static BackPackConnectionResult Failed(string error) =>
            new BackPackConnectionResult(false, error ?? "unknown_error");

        public override string ToString() => IsConnected
            ? "BackPackConnection(ok)"
            : $"BackPackConnection(failed, error={Error})";
    }

    // The result of deleting ONE backpack item (see DeleteItemAsync).
    //
    // IsDeleted only if the server reported deleting exactly this instance NOW
    // (outcome "item_deleted"). The status code decides nothing: the server
    // answers 200 both when Backpack failed to delete the item ("backpack_failed")
    // and when the item was already gone ("item_already_gone"). The game grants
    // the product based on IsDeleted — so no answer may ever err towards "deleted".
    public readonly struct BackPackDeleteResult
    {
        public const string OutcomeDeleted = "item_deleted";
        public const string OutcomeAlreadyGone = "item_already_gone";

        public bool IsDeleted { get; }

        // What the server said about this item ("item_already_gone",
        // "backpack_failed"), or null if there was no report.
        public string Outcome { get; }

        // For the log: the reason when not deleted.
        public string Error { get; }

        private BackPackDeleteResult(bool isDeleted, string outcome, string error)
        {
            IsDeleted = isDeleted;
            Outcome = outcome;
            Error = error;
        }

        public static BackPackDeleteResult Deleted() => new BackPackDeleteResult(true, OutcomeDeleted, null);

        public static BackPackDeleteResult Failed(string error, string outcome = null) =>
            new BackPackDeleteResult(false, outcome, error ?? "unknown_error");

        public override string ToString() => IsDeleted
            ? "BackPackDelete(deleted)"
            : $"BackPackDelete(not deleted, outcome={Outcome ?? "none"}, error={Error})";
    }

    // One item in the player's backpack — an element of items in the reply to
    // GET /rewards?xsollaId=…&gameId=….
    //
    // Field names are the server contract, as for BackPackEventBody. Fields rather
    // than properties — for the same reason as the other DTOs here: Newtonsoft
    // fills them without worrying about setter stripping under IL2CPP.
    //
    // Sku is the item type, Id is the concrete instance in the backpack (items are
    // deleted by it: deleting by sku removes ALL items of that type).
    //
    // RawAttributes and RawCustomAttributes are Backpack's own JSON, passed through by
    // the server unparsed (PlayerRewardItem). BackPackService parses them into
    // Attributes / CustomAttributes (see BackPackAttributes).
    //
    // The package does not interpret attributes: which ones exist ("reward", "merge", …)
    // and what their values mean is up to the client, e.g.
    // CustomAttributes.ValueIds("reward") or CustomAttributes.Find("merge").
    public sealed class BackPackRewardItem
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("sku")] public string Sku;
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("iconUrl")] public string IconUrl;

        // ISO 8601 as the server wrote it; kept as text — nothing here sorts by it.
        [JsonProperty("createdAt")] public string CreatedAt;

        // The Xsolla project the item was granted in; null if the server didn't say.
        // The backpack holds items of every project, so this tells this game's items from others'.
        [JsonProperty("projectId")] public long? ProjectId;

        [JsonProperty("attributes")] public JToken RawAttributes;
        [JsonProperty("customAttributes")] public JToken RawCustomAttributes;

        // Filled by BackPackService from the raw JSON above; never null.
        [JsonIgnore] public IReadOnlyList<BackPackAttribute> Attributes = BackPackAttributes.None;
        [JsonIgnore] public IReadOnlyList<BackPackAttribute> CustomAttributes = BackPackAttributes.None;
    }

    // A parsed reply to the backpack request.
    //
    // Items is never null: on failure it is an empty list, so the screen can
    // redraw from it without checks. Error is for the log, not for the player:
    // "no_xsolla_id", "not_configured", "http_400: invalid_xsolla_id"…
    public readonly struct BackPackRewardsReply
    {
        private static readonly BackPackRewardItem[] NoItems = Array.Empty<BackPackRewardItem>();

        public bool Success { get; }
        public IReadOnlyList<BackPackRewardItem> Items { get; }

        // 0 — the server was never reached (no network, not configured, no xsollaID).
        public long StatusCode { get; }
        public string Error { get; }

        private BackPackRewardsReply(bool success, IReadOnlyList<BackPackRewardItem> items, long statusCode, string error)
        {
            Success = success;
            Items = items ?? NoItems;
            StatusCode = statusCode;
            Error = error;
        }

        public static BackPackRewardsReply Ok(IReadOnlyList<BackPackRewardItem> items) =>
            new BackPackRewardsReply(true, items, 200, null);

        public static BackPackRewardsReply Failed(string error, long statusCode = 0) =>
            new BackPackRewardsReply(false, null, statusCode, error ?? "unknown_error");

        public override string ToString() => Success
            ? $"BackPackRewardsReply(ok, items={Items.Count})"
            : $"BackPackRewardsReply(failed, status={StatusCode}, error={Error})";
    }
}
