using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.Backpack
{
    // The game-facing API of the BackPack server: events, backpack contents,
    // spending an item, connection check. Debug deletions are not part of it —
    // they live in BackPackDebugApi and are not included in release builds.
    //
    // No exceptions escape: any failure comes back as a result with an Error.
    // The only exception is OperationCanceledException, when the caller cancelled
    // its own cancellationToken.
    public interface IBackPackService
    {
        // Whether the service is configured: it has a baseUrl and a gameId.
        bool IsAvailable { get; }

        // Whether there is someone to send on behalf of. Separate from IsAvailable:
        // a guest is not a broken config but the normal state before sign-in.
        bool HasXsollaId { get; }

        string XsollaId { get; }

        // POST {eventsPath}. eventName is the event name on the server side; for
        // achievements it is their id. eventId is deterministic by default — see
        // BackPackService.StableEventId.
        Task<BackPackReply> SendEventAsync(
            string eventName, string eventId = null, CancellationToken cancellationToken = default);

        // GET {rewardsPath}?xsollaId=…&gameId=… — what is in the backpack.
        Task<BackPackRewardsReply> GetRewardsAsync(CancellationToken cancellationToken = default);

        // Spend ONE item instance. Cancelling a request already sent is dangerous:
        // the server may have deleted the item, and nobody would find out.
        Task<BackPackDeleteResult> DeleteItemAsync(string itemId, CancellationToken cancellationToken = default);

        // Whether the server answers — before taking the player to the backpack.
        Task<BackPackConnectionResult> CheckConnectionAsync(CancellationToken cancellationToken = default);
    }
}
