using System;
using System.Threading.Tasks;

namespace LongLeap.DailyQuests
{
    public interface IDailyQuestService
    {
        /// <summary>
        /// Something changed: progress, completion, a claim, a new set.
        /// The UI just re-reads <see cref="GetActive"/> in response. A stat change
        /// that did not move the visible progress raises no event.
        /// </summary>
        event Action Changed;

        Task InitializeAsync();

        void Shutdown();

        /// <summary>
        /// Quests of the current period in issue order. While nothing has changed,
        /// the same array and the same <see cref="QuestStatus"/> objects are returned — do not
        /// modify them.
        /// </summary>
        QuestStatus[] GetActive();

        /// <summary>Status of an active quest, or <c>null</c> if there is no such quest now.</summary>
        QuestStatus GetStatus(QuestId questId);

        /// <summary>How many rewards are waiting to be claimed — for the button badge.</summary>
        int ClaimableCount { get; }

        /// <summary>Time left until the set changes.</summary>
        TimeSpan TimeUntilReset { get; }

        /// <summary>
        /// Check whether a new period has started and, if so, issue a set
        /// (<see cref="Changed"/> follows). Cheap: call it from Update while the
        /// quests UI is open — otherwise the set changes only on the next
        /// access to the service.
        /// </summary>
        void Tick();

        /// <summary>
        /// Claim the reward of a completed quest. The reward is granted only
        /// after the "claimed" mark has been written to the store.
        /// <c>false</c> — the quest is not active, not complete, already claimed, or
        /// the mark could not be written; no reward is granted then.
        /// </summary>
        Task<bool> ClaimAsync(QuestId questId);
    }
}
