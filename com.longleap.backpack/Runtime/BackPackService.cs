using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LongLeap.Backpack
{
    // IBackPackService implemented on top of IBackPackTransport.
    //
    // The service has no defaults of its own: the address, paths and gameId come
    // from outside, from BackPackEndpoint (BackPackConfigSO.Active). It must not
    // know which machine the server runs on — otherwise changing the host would be
    // a code change rather than an asset change.
    //
    // Its own transport rather than XsollaIdHttp: that assembly
    // (LongLeap.XsollaId.Web) lives under the XSOLLA_ID defineConstraints, and
    // relying on it would switch event sending off together with sign-in.
    //
    // Dispose aborts everything still in flight, and no further requests go out:
    // they answer Failed("disposed"). The service container calls it on quit.
    // IsAvailable deliberately does not change: otherwise the achievement wrapper
    // would take the stopped service for a switched-off one and save the unlock
    // without its event — and the reward for it would be lost.
    public sealed class BackPackService : IBackPackService, IDisposable
    {
        // The player who pressed "Start" waits on the connection check — they cannot
        // be held longer. If the server does not make it in time, we treat it as no
        // connection and the game starts without it.
        private const int ConnectionCheckTimeoutSeconds = 5;

        private readonly BackPackClient _client;

        // endpoint == null or without baseUrl/gameId — the service is off:
        // IsAvailable is false and every request answers "not_configured" at once.
        //
        // transport is replaced by tests; null means UnityWebRequestBackPackTransport.
        public BackPackService(
            Func<string> xsollaIdProvider,
            BackPackEndpoint endpoint,
            IBackPackTransport transport = null,
            Func<bool, Task<string>> userTokenProvider = null)
        {
            _client = new BackPackClient(xsollaIdProvider, endpoint, transport, userTokenProvider);
        }

        public bool IsAvailable => _client.IsConfigured;

        public bool HasXsollaId => !string.IsNullOrEmpty(XsollaId);

        public string XsollaId => _client.XsollaId;

        public void Dispose() => _client.Stop();

        public async Task<BackPackReply> SendEventAsync(
            string eventName, string eventId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(eventName))
                return BackPackReply.Failed("empty_event_name");

            string notSendable = _client.WhyNotSendable(out string xsollaId);

            if (notSendable != null)
                return BackPackReply.Failed(notSendable);

            string body = JsonConvert.SerializeObject(new BackPackEventBody
            {
                GameId = _client.GameId,
                XsollaId = xsollaId,
                Event = eventName,
                EventId = string.IsNullOrEmpty(eventId) ? StableEventId(_client.GameId, xsollaId, eventName) : eventId
            });

            BackPackHttpReply raw = await _client.SendAsync(
                BackPackHttpCall.Post, _client.EventsUrl, body, BackPackClient.DefaultTimeoutSeconds, cancellationToken);

            return Parse(raw);
        }

        // Codes the server returns: 400 invalid_xsolla_id, 404 unknown_game,
        // 502 — Backpack itself failed.
        public async Task<BackPackRewardsReply> GetRewardsAsync(CancellationToken cancellationToken = default)
        {
            string notSendable = _client.WhyNotSendable(out string xsollaId);

            if (notSendable != null)
                return BackPackRewardsReply.Failed(notSendable);

            var url = BackPackClient.Query(_client.RewardsUrl, "xsollaId", xsollaId);
            BackPackClient.Append(url, "gameId", _client.GameId);

            BackPackHttpReply raw = await _client.SendAsync(
                BackPackHttpCall.Get, url.ToString(), null, BackPackClient.DefaultTimeoutSeconds, cancellationToken);

            return ParseRewards(raw);
        }

        // Hits the same backpack endpoint, just without the xsollaID, rather than
        // the server root: this endpoint certainly has CORS set up (the game uses
        // it anyway), and without CORS the browser in WebGL would drop the answer
        // and a live server would look unreachable. Without an xsollaID the server
        // answers 400 — that is exactly "connected": what matters is that there is
        // an answer, not its content.
        //
        // NOT connected when there was no answer at all (network, timeout) or the
        // code is 5xx: 502/522 come both from Cloudflare when the server behind it
        // is down and from the server itself when its Backpack failed — the
        // backpack will not load in either case.
        //
        // 404 is not connected either: either there is no endpoint at this address
        // (wrong path or host, a Cloudflare stub page) or the server does not know
        // our gameId (unknown_game). There is an answer, but the backpack will
        // never load from it. The body goes into Error so the log shows which case.
        public async Task<BackPackConnectionResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
        {
            // The check needs no xsollaID — it asks without one (see above).
            if (_client.IsStopped)
                return BackPackConnectionResult.Failed(BackPackClient.Disposed);

            if (!IsAvailable)
                return BackPackConnectionResult.Failed(BackPackClient.NotConfigured);

            var url = BackPackClient.Query(_client.RewardsUrl, "gameId", _client.GameId);

            BackPackHttpReply raw = await _client.SendAsync(
                BackPackHttpCall.Get, url.ToString(), null, ConnectionCheckTimeoutSeconds, cancellationToken);

            if (raw.IsNotSent)
                return BackPackConnectionResult.Failed(raw.NotSentReason);

            if (raw.IsTransportError)
                return BackPackConnectionResult.Failed($"transport: {raw.TransportError}");

            if (raw.StatusCode == 404)
                return BackPackConnectionResult.Failed($"http_404: {raw.Body}");

            if (raw.StatusCode >= 500)
                return BackPackConnectionResult.Failed($"http_{raw.StatusCode}");

            return BackPackConnectionResult.Connected();
        }

        // Only by itemId: by sku the server removes every item of that type and also
        // clears reward_claims (the reward could be earned again). With a single
        // itemId neither happens.
        //
        // Whether the item was deleted is decided by the report in the body, not by
        // the status code: the server answers 200 for every outcome (see
        // BackPackDeleteResult).
        public async Task<BackPackDeleteResult> DeleteItemAsync(
            string itemId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(itemId))
                return BackPackDeleteResult.Failed("no_item_id");

            string notSendable = _client.WhyNotSendable(out string xsollaId);

            if (notSendable != null)
                return BackPackDeleteResult.Failed(notSendable);

            var raw = await _client.DeleteItemsAsync(xsollaId, null, itemId, cancellationToken);

            return ParseDelete(raw, itemId);
        }

        private static BackPackReply Parse(BackPackHttpReply raw)
        {
            if (raw.IsNotSent)
                return BackPackReply.Failed(raw.NotSentReason);

            if (raw.IsTransportError)
                return BackPackReply.Failed($"transport: {raw.TransportError}");

            if (string.IsNullOrWhiteSpace(raw.Body))
                return BackPackReply.Failed($"http_{raw.StatusCode}_empty_body");

            BackPackEventReplyBody parsed;

            try
            {
                parsed = JsonConvert.DeserializeObject<BackPackEventReplyBody>(raw.Body);
            }
            catch (Exception e)
            {
                return BackPackReply.Failed($"bad_json: {e.Message}");
            }

            if (parsed == null)
                return BackPackReply.Failed($"http_{raw.StatusCode}_bad_json");

            // The body is parsed even for an unsuccessful status: that is exactly
            // where the server puts the refusal reason ("invalid_client"). Success
            // requires both signs to agree — success: true with a 500 is a mismatch,
            // and nothing may be saved on it.
            if (!parsed.Success)
                return BackPackReply.Failed(parsed.Error ?? $"http_{raw.StatusCode}_rejected");

            if (!raw.IsHttpSuccess)
                return BackPackReply.Failed($"http_{raw.StatusCode}_with_success_body");

            return BackPackReply.Ok(parsed.RewardGranted, parsed.RewardId);
        }

        private static BackPackDeleteResult ParseDelete(BackPackHttpReply raw, string itemId)
        {
            if (raw.IsNotSent)
                return BackPackDeleteResult.Failed(raw.NotSentReason);

            if (raw.IsTransportError)
                return BackPackDeleteResult.Failed($"transport: {raw.TransportError}");

            if (!raw.IsHttpSuccess)
                return BackPackDeleteResult.Failed($"http_{raw.StatusCode}: {raw.Body}");

            if (string.IsNullOrWhiteSpace(raw.Body))
                return BackPackDeleteResult.Failed($"http_{raw.StatusCode}_empty_body");

            BackPackDeleteReplyBody parsed;

            try
            {
                parsed = JsonConvert.DeserializeObject<BackPackDeleteReplyBody>(raw.Body);
            }
            catch (Exception e)
            {
                return BackPackDeleteResult.Failed($"bad_json: {e.Message}");
            }

            // No report about OUR item — the server did not touch it (did not find it,
            // or deleted something else). That must not count as a deletion.
            if (parsed?.Items != null)
            {
                foreach (var entry in parsed.Items)
                {
                    if (entry == null || !string.Equals(entry.ItemId, itemId, StringComparison.Ordinal))
                        continue;

                    return string.Equals(entry.Outcome, BackPackDeleteResult.OutcomeDeleted, StringComparison.Ordinal)
                        ? BackPackDeleteResult.Deleted()
                        : BackPackDeleteResult.Failed(entry.Error ?? entry.Outcome ?? "no_outcome", entry.Outcome);
                }
            }

            return BackPackDeleteResult.Failed($"item_not_in_report: {raw.Body}");
        }

        private static BackPackRewardsReply ParseRewards(BackPackHttpReply raw)
        {
            if (raw.IsNotSent)
                return BackPackRewardsReply.Failed(raw.NotSentReason);

            if (raw.IsTransportError)
                return BackPackRewardsReply.Failed($"transport: {raw.TransportError}");

            // The server puts the error code ("invalid_xsolla_id") in the body — it is
            // passed on whole: we do not know the error body's shape, and the log is
            // where it is needed most.
            if (!raw.IsHttpSuccess)
                return BackPackRewardsReply.Failed($"http_{raw.StatusCode}: {raw.Body}", raw.StatusCode);

            if (string.IsNullOrWhiteSpace(raw.Body))
                return BackPackRewardsReply.Failed($"http_{raw.StatusCode}_empty_body", raw.StatusCode);

            BackPackRewardsReplyBody parsed;

            try
            {
                parsed = JsonConvert.DeserializeObject<BackPackRewardsReplyBody>(raw.Body, RewardsJson);
            }
            catch (Exception e)
            {
                return BackPackRewardsReply.Failed($"bad_json: {e.Message}", raw.StatusCode);
            }

            if (parsed == null)
                return BackPackRewardsReply.Failed($"http_{raw.StatusCode}_bad_json", raw.StatusCode);

            // Empty elements (null in the array) are dropped here, so the screen does
            // not have to check each one.
            var items = new List<BackPackRewardItem>();

            if (parsed.Items != null)
            {
                foreach (var item in parsed.Items)
                {
                    if (item == null)
                        continue;

                    item.Attributes = BackPackAttributes.Parse(item.RawAttributes);
                    item.CustomAttributes = BackPackAttributes.Parse(item.RawCustomAttributes);

                    items.Add(item);
                }
            }

            return BackPackRewardsReply.Ok(items);
        }

        // eventId is the idempotency key, so it is DETERMINISTIC rather than
        // Guid.NewGuid() per attempt.
        //
        // The reason is the "save only on a successful answer" rule: until there is
        // an answer the achievement is not saved, and next session the event is sent
        // again. With a random id the server would see two different events and
        // could grant the reward twice; with this one it is the same event, and the
        // server-side deduplication kicks in.
        //
        // The hash is not cryptographic (FNV-1a with two seeds) because
        // System.Security.Cryptography is not yet confirmed to work under
        // IL2CPP/WebGL in this project (see the warning in Pkce), and a
        // deduplication key needs no strength — only reproducibility and no
        // accidental collisions.
        public static string StableEventId(string gameId, string xsollaId, string eventName)
        {
            byte[] payload = Encoding.UTF8.GetBytes($"{gameId}|{xsollaId}|{eventName}");
            var bytes = new byte[16];

            BitConverter.GetBytes(Fnv1a64(payload, 0xcbf29ce484222325UL)).CopyTo(bytes, 0);
            BitConverter.GetBytes(Fnv1a64(payload, 0x9e3779b97f4a7c15UL)).CopyTo(bytes, 8);

            // Shape it as a version 3 (name-based) UUID: the server expects a uuid,
            // not arbitrary 32 hex characters.
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

        // The reply body of DELETE /backpack/items: a report per item.
        private sealed class BackPackDeleteReplyBody
        {
            [JsonProperty("items")] public BackPackDeleteReplyItem[] Items;
        }

        private sealed class BackPackDeleteReplyItem
        {
            [JsonProperty("itemId")] public string ItemId;
            [JsonProperty("outcome")] public string Outcome;
            [JsonProperty("error")] public string Error;
        }

        // Dates stay text: by default Newtonsoft turns an ISO string into a DateTime and
        // back into a culture-dependent string, both in CreatedAt and inside attributes.
        private static readonly JsonSerializerSettings RewardsJson = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None
        };

        // The reply body of GET /rewards. Private, like BackPackEventReplyBody:
        // BackPackRewardsReply is what goes outside.
        private sealed class BackPackRewardsReplyBody
        {
            [JsonProperty("xsollaId")] public string XsollaId;
            [JsonProperty("gameId")] public string GameId;
            [JsonProperty("items")] public BackPackRewardItem[] Items;
        }

        // The event reply body. A private DTO: BackPackReply is what goes outside, so
        // the shape of the server's json does not spread through the game.
        private sealed class BackPackEventReplyBody
        {
            [JsonProperty("success")] public bool Success;
            [JsonProperty("rewardGranted")] public bool RewardGranted;
            [JsonProperty("rewardId")] public string RewardId;
            [JsonProperty("error")] public string Error;
        }
    }
}
