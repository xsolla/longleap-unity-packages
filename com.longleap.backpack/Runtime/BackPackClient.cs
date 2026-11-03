using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace LongLeap.Backpack
{
    // What BackPackService and BackPackDebugApi share: endpoint URLs, gameId,
    // on whose behalf and through which transport. Internal — only the facades
    // face outward.
    //
    // No defaults of its own: the address, paths and gameId come from
    // BackPackEndpoint (that is, from BackPackConfigSO). An empty baseUrl or
    // gameId is the regular "off" switch: there are no URLs and no request goes out.
    internal sealed class BackPackClient
    {
        public const int DefaultTimeoutSeconds = 10;

        public const string NotConfigured = "not_configured";
        public const string NoXsollaId = "no_xsolla_id";
        public const string Disposed = "disposed";

        private readonly Func<string> _xsollaIdProvider;
        private readonly IBackPackTransport _transport;

        // The owner's lifetime: Stop aborts everything still in flight. Not
        // disposed: without a timer a CancellationTokenSource holds nothing, and
        // Token on a disposed one would throw ObjectDisposedException.
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        // xsollaIdProvider is a delegate rather than a string: the xsollaID does
        // not exist when the service is created — it appears when the player signs
        // in (or a session is silently restored) and changes on sign-out/sign-in.
        public BackPackClient(Func<string> xsollaIdProvider, BackPackEndpoint endpoint, IBackPackTransport transport)
        {
            _xsollaIdProvider = xsollaIdProvider;
            _transport = transport ?? new UnityWebRequestBackPackTransport();

            string baseUrl = endpoint?.BaseUrl;
            string gameId = endpoint?.GameId;

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(gameId))
                return;

            baseUrl = baseUrl.TrimEnd('/');
            GameId = gameId;
            EventsUrl = baseUrl + endpoint.EventsPath;
            RewardsUrl = baseUrl + endpoint.RewardsPath;
            ItemsUrl = baseUrl + endpoint.ItemsPath;
        }

        public string GameId { get; }
        public string EventsUrl { get; }
        public string RewardsUrl { get; }
        public string ItemsUrl { get; }

        public bool IsConfigured => GameId != null;

        public bool IsStopped => _lifetime.IsCancellationRequested;

        public string XsollaId => _xsollaIdProvider?.Invoke();

        // Why a request on the player's behalf would not go out now, or null if it
        // would. Order matters: "stopped" and "not configured" take precedence over
        // "guest".
        public string WhyNotSendable(out string xsollaId)
        {
            xsollaId = null;

            if (IsStopped)
                return Disposed;

            if (!IsConfigured)
                return NotConfigured;

            xsollaId = XsollaId;

            return string.IsNullOrEmpty(xsollaId) ? NoXsollaId : null;
        }

        public void Stop() => _lifetime.Cancel();

        // url?first=value — the first parameter is always present (xsollaId or gameId).
        public static StringBuilder Query(string url, string name, string value) =>
            new StringBuilder(url).Append('?').Append(name).Append('=').Append(UnityWebRequest.EscapeURL(value));

        public static void Append(StringBuilder url, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            url.Append('&').Append(name).Append('=').Append(UnityWebRequest.EscapeURL(value));
        }

        // DELETE {itemsPath}?xsollaId=…[&sku=…][&itemId=…]
        //
        // Shared by spending one item (BackPackService.DeleteItemAsync) and the
        // debug backpack wipe (BackPackDebugApi). Without sku and itemId — the
        // whole backpack. Preconditions (configured, has an xsollaID) are checked
        // by the caller: each facade answers them in its own result shape.
        public Task<BackPackHttpReply> DeleteItemsAsync(
            string xsollaId, string sku, string itemId, CancellationToken cancellationToken)
        {
            var url = Query(ItemsUrl, "xsollaId", xsollaId);
            Append(url, "sku", sku);
            Append(url, "itemId", itemId);

            return SendAsync(BackPackHttpCall.Delete, url.ToString(), null, DefaultTimeoutSeconds, cancellationToken);
        }

        // No exceptions escape except the cancellation the caller asked for: any
        // other failure comes back as a reply. Sending is called from background
        // tasks and button handlers, where a thrown exception would vanish silently.
        //
        // Stop is not the caller's cancellation: the request is aborted but comes
        // back as a transport error. Otherwise an achievement whose event was sent
        // right before quitting would throw into AchievementSystem.
        public async Task<BackPackHttpReply> SendAsync(
            string method, string url, string body, int timeoutSeconds, CancellationToken cancellationToken)
        {
            if (IsStopped)
                return BackPackHttpReply.NotSent(Disposed);

            var call = new BackPackHttpCall(method, url, body, timeoutSeconds);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);

            try
            {
                return await _transport.SendAsync(call, linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (IsStopped)
            {
                return BackPackHttpReply.Transport($"{call} — aborted: {Disposed}");
            }
            catch (Exception e)
            {
                return BackPackHttpReply.Transport($"{call} — {e.Message}");
            }
        }
    }
}
