#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.Backpack
{
    // Destructive endpoints for testing and debugging: wipe the player's
    // backpack, reset the records of granted rewards.
    //
    // Separate from IBackPackService and only in the Editor and development
    // builds: DeleteBackpackItemsAsync() without filters wipes the whole backpack,
    // and a release build has no business with such a button. Code that calls
    // this class must also live under #if UNITY_EDITOR || DEVELOPMENT_BUILD.
    //
    // The reply is returned RAW (BackPackHttpReply; ToString is fit for the log)
    // rather than parsed: the body is a report with a list of lines, and it is
    // needed precisely to go into the log as a whole.
    //
    // Both endpoints require an xsollaID — it is always one player's progress
    // that gets deleted; there is no "delete for everyone" here and there must
    // never be.
    public sealed class BackPackDebugApi
    {
        private readonly BackPackClient _client;

        public BackPackDebugApi(
            Func<string> xsollaIdProvider,
            BackPackEndpoint endpoint,
            IBackPackTransport transport = null,
            Func<bool, Task<string>> userTokenProvider = null)
        {
            _client = new BackPackClient(xsollaIdProvider, endpoint, transport, userTokenProvider);
        }

        // DELETE {itemsPath}?xsollaId=…[&sku=…][&itemId=…]
        //
        // Without sku and itemId — the whole backpack. By sku the server also
        // clears the reward_claims of that type: the reward can be earned again.
        public Task<BackPackHttpReply> DeleteBackpackItemsAsync(
            string sku = null, string itemId = null, CancellationToken cancellationToken = default)
        {
            string notSendable = _client.WhyNotSendable(out string xsollaId);

            return notSendable != null
                ? Task.FromResult(BackPackHttpReply.NotSent(notSendable))
                : _client.DeleteItemsAsync(xsollaId, sku, itemId, cancellationToken);
        }

        // DELETE {eventsPath}?xsollaId=&gameId=…[&eventId=…][&sku=…]&backpack=&force=
        //
        // Exists on the server only in the Development environment (others answer
        // 404). Removes the records of granted rewards — what the server uses to
        // guard against granting twice. Without them the same events grant the
        // reward again, which is usually exactly what testing needs.
        //
        // gameId is always added automatically: the same player's other games have
        // nothing to do with our testing, and wiping them with one button is too much.
        //
        // backpack = true — the server also revokes the item itself; force = false —
        // records whose item could not be revoked stay in place (otherwise granting
        // again would create a duplicate).
        public Task<BackPackHttpReply> DeleteClaimsAsync(
            string eventId = null,
            string sku = null,
            bool backpack = true,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            string notSendable = _client.WhyNotSendable(out string xsollaId);

            if (notSendable != null)
                return Task.FromResult(BackPackHttpReply.NotSent(notSendable));

            var url = BackPackClient.Query(_client.EventsUrl, "xsollaId", xsollaId);
            BackPackClient.Append(url, "gameId", _client.GameId);
            BackPackClient.Append(url, "eventId", eventId);
            BackPackClient.Append(url, "sku", sku);

            url.Append("&backpack=").Append(backpack ? "true" : "false")
               .Append("&force=").Append(force ? "true" : "false");

            return _client.SendAsync(
                BackPackHttpCall.Delete, url.ToString(), null, BackPackClient.DefaultTimeoutSeconds, cancellationToken);
        }
    }
}
#endif
