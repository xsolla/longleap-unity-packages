using System;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Сохраняемое состояние. Сам прогресс не пишется — он выводится из статов.
    /// Хранится только то, из чего его вывести (точка отсчёта), и то, чего из
    /// статов не восстановить (выполнен, забран).
    /// </summary>
    [Serializable]
    public sealed class DailyQuestsState
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;

        /// <summary>Начало периода, на который выдан текущий набор (UTC ticks). 0 — набора ещё не было.</summary>
        public long PeriodStartUtcTicks;

        public ActiveQuestState[] Active = Array.Empty<ActiveQuestState>();

        public static DailyQuestsState Empty() => new DailyQuestsState();
    }

    [Serializable]
    public sealed class ActiveQuestState
    {
        public string QuestId;

        /// <summary>
        /// Цели были набраны. Защёлка: после выполнения квест остаётся
        /// выполненным, даже если стат потом уменьшится (Absolute-режим).
        /// </summary>
        public bool IsCompleted;

        public bool IsClaimed;

        public ObjectiveBaseline[] Baselines = Array.Empty<ObjectiveBaseline>();
    }

    /// <summary>Значение стата в момент выдачи квеста — от него считается Delta.</summary>
    [Serializable]
    public sealed class ObjectiveBaseline
    {
        public string ObjectiveId;
        public long Value;
    }
}
