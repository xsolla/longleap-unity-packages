using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.Quests
{
    // The game-facing API of the Quests service. No exceptions escape except the
    // cancellation the caller asked for: any failure comes back as a reply.
    public interface IQuestService
    {
        // Whether the service is configured: it has an address and a game id.
        bool IsAvailable { get; }

        // Whether there is a signed-in player to send on behalf of.
        bool HasXsollaId { get; }

        // POST /v1/users/{xsolla_id}/quests/events — one completed quest event.
        Task<QuestReply> SendEventAsync(
            string eventName,
            IReadOnlyDictionary<string, string> properties = null,
            CancellationToken cancellationToken = default);

        // GET /v1/quests — the project's public quests (page from 1, limit 1–100).
        Task<QuestPageReply> GetQuestsAsync(int page = 1, int limit = 10, CancellationToken cancellationToken = default);

        // GET /v1/users/{xsolla_id}/quests with the player's Login token in
        // X-Player-Login-Token — the same page, each quest with the player's progress.
        Task<QuestPageReply> GetPlayerQuestsAsync(
            string loginToken, int page = 1, int limit = 10, CancellationToken cancellationToken = default);
    }
}
