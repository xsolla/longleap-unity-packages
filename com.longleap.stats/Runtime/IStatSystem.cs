using System;

namespace LongLeap.Stats
{
    public interface IStatSystem
    {
        void Define(StatPath path, StatType type);

        /// <summary>
        /// Whether the path is defined. For callers that build the path from data
        /// (upgrade id, mob type): Set/Add on an undefined path throw, and Get
        /// returns 0, which can't be told apart from "defined but empty".
        /// </summary>
        bool IsDefined(StatPath path);

        void Set(StatPath path, long value);
        void Add(StatPath path, long delta);
        long Get(StatPath path);
        void Subscribe(StatPath pattern, Action<StatPath, long> callback);
        void Unsubscribe(StatPath pattern, Action<StatPath, long> callback);
        void UnsubscribeAll();
        long GetAggregate(StatPath pattern);
    }
}
