using System;
using System.Threading.Tasks;
using LongLeap.Achievements;
using UnityEngine;

namespace LongLeap.Backpack
{
    // A wrapper over the achievement store: first the event goes to the BackPack
    // server, and only on a successful answer is the unlock written to the store.
    //
    // Why a wrapper and not the IAchievementGameBridge: the bridge is called for
    // notifications and cannot cancel anything — AchievementSystem writes to the
    // store in its PersistUnlockAsync regardless of it. There is exactly one point
    // where "save" can still NOT happen — MarkUnlockedAsync.
    //
    // What happens when the server refuses: in memory the achievement stays
    // unlocked for this session (AchievementSystem marks it before calling the
    // store — that cannot be undone without rewriting the system), but it does not
    // reach disk or the cloud. On the next launch it is locked again, stats unlock
    // it once more, and the event is re-sent — with the same eventId, so the server
    // does not grant the reward twice (BackPackService.StableEventId).
    public sealed class BackPackAchievementStateStore : IAchievementStateStore
    {
        private readonly IAchievementStateStore _inner;
        private readonly IBackPackService _backPack;

        public BackPackAchievementStateStore(IAchievementStateStore inner, IBackPackService backPack)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _backPack = backPack;
        }

        public Task<AchievementStatesSnapshot> LoadAsync(AchievementDefinition[] definitions) =>
            _inner.LoadAsync(definitions);

        public async Task MarkUnlockedAsync(AchievementId id, long timestamp)
        {
            // A guest's unlocks are saved as before, without the server.
            //
            // This is NOT a shortcut: before sign-in the xsollaID does not exist at
            // all. If we required a successful answer here too, a player without an
            // account would lose every achievement on restart, and hero unlock
            // conditions would stay unmet. The guest gets no reward anyway: there is
            // no event without an xsollaID, so the server does not know about it and
            // will not grant it after sign-in — if that is needed, the event has to
            // be re-sent separately on SignedIn.
            if (_backPack == null || !_backPack.IsAvailable || !_backPack.HasXsollaId)
            {
                await _inner.MarkUnlockedAsync(id, timestamp);
                return;
            }

            BackPackReply reply = await _backPack.SendEventAsync(id.Value);

            if (!reply.Success)
            {
                // Warning rather than Error: a refusal is a normal situation (the
                // server is down, the client is unknown to it), and the achievement
                // comes back here next session.
                Debug.LogWarning(
                    $"BackPack: event '{id.Value}' was not accepted ({reply.Error}) — the achievement is not saved, will retry next session.");
                return;
            }

            if (reply.RewardGranted)
                Debug.Log($"BackPack: reward '{reply.RewardId ?? "?"}' granted for '{id.Value}'.");

            await _inner.MarkUnlockedAsync(id, timestamp);
        }

        // The reset is local only. The server deliberately does not know about it:
        // granted rewards cannot be revoked, and the eventId stays the same, so after
        // a reset repeated events are simply handled as duplicates.
        public Task DeleteAllAsync() => _inner.DeleteAllAsync();
    }
}
