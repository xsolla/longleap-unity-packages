using System;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Persisted state. Progress itself is not written — it is derived from stats.
    /// Only what it is derived from (the baseline) is stored, plus what cannot
    /// be recovered from stats (completed, claimed).
    /// </summary>
    [Serializable]
    public sealed class DailyQuestsState
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;

        /// <summary>Start of the period the current set was issued for (UTC ticks). 0 — no set yet.</summary>
        public long PeriodStartUtcTicks;

        /// <summary>
        /// No set is issued for this period on purpose (the save was unreadable — see
        /// <see cref="DailyQuestStateLoadStatus.Unreadable"/>): do not refill it until
        /// the next reset.
        /// </summary>
        public bool Withheld;

        public ActiveQuestState[] Active = Array.Empty<ActiveQuestState>();

        public static DailyQuestsState Empty() => new DailyQuestsState();
    }

    [Serializable]
    public sealed class ActiveQuestState
    {
        public string QuestId;

        /// <summary>
        /// The objectives were met. A latch: once completed the quest stays
        /// completed even if the stat drops later (Absolute mode).
        /// </summary>
        public bool IsCompleted;

        public bool IsClaimed;

        public ObjectiveBaseline[] Baselines = Array.Empty<ObjectiveBaseline>();
    }

    /// <summary>Stat value at the moment the quest was issued — Delta counts from it.</summary>
    [Serializable]
    public sealed class ObjectiveBaseline
    {
        public string ObjectiveId;
        public long Value;

        /// <summary>
        /// The objective is met. A latch per objective: "10k in a run + 30
        /// grinds" counts even if the grinds were finished in another run.
        /// </summary>
        public bool IsCompleted;
    }
}
