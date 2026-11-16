using System;
using System.Threading.Tasks;

namespace LongLeap.Stats
{
    public readonly struct StatValue
    {
        public bool Exists { get; }
        public long Value { get; }

        private StatValue(bool exists, long value)
        {
            Exists = exists;
            Value = value;
        }

        public static StatValue None => default;
        public static StatValue Of(long value) => new(true, value);
    }

    public interface IStatPersistence
    {
        Task SaveAsync(StatPath path, long value);
        Task<StatValue> LoadAsync(StatPath path);
    }
}
