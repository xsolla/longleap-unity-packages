using System;

namespace LongLeap.Stats
{
    public interface IStatSystem
    {
        void Define(StatPath path, StatType type);

        /// <summary>
        /// Объявлен ли путь. Нужен вызывающему, который строит путь из данных
        /// (id прокачки, тип моба): Set/Add по необъявленному пути бросают
        /// исключение, а Get вернёт 0 и от «объявлен, но пуст» не отличит.
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
