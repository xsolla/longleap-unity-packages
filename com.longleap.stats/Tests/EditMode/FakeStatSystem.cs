using System;
using System.Collections.Generic;
using System.Linq;
using LongLeap.Stats;

namespace LongLeap.Stats.Tests
{
    public sealed class FakeStatSystem : IStatSystem
    {
        private readonly Dictionary<string, List<Action<StatPath, long>>> _subs = new();
        private readonly Dictionary<string, long> _values = new();
        private readonly HashSet<string> _knownPaths = new();

        public void Define(StatPath path, StatType type)
        {
            _knownPaths.Add(path.Value);
        }

        public bool IsDefined(StatPath path) => _knownPaths.Contains(path.Value);

        public void Set(StatPath path, long value)
        {
            SetValue(path, value);
        }

        public void Add(StatPath path, long delta)
        {
            long current = _values.TryGetValue(path.Value, out var v) ? v : 0;
            SetValue(path, current + delta);
        }

        public long Get(StatPath path)
        {
            return _values.TryGetValue(path.Value, out var v) ? v : 0;
        }

        public void SetValue(StatPath path, long value)
        {
            _values[path.Value] = value;

            var changed = new List<string> { path.Value };
            var p = path.Parent;
            while (!p.IsNone)
            {
                changed.Add(p.Value);
                p = p.Parent;
            }

            foreach (var ancestor in changed.Skip(1))
            {
                var children = _values.Keys
                    .Where(k => k.StartsWith(ancestor + ":") && !k.Substring((ancestor + ":").Length).Contains(':'))
                    .Select(k => _values[k])
                    .ToList();

                _values[ancestor] = children.Count > 0 ? children.Sum() : 0;
            }

            foreach (var changedPath in changed)
            {
                if (_subs.TryGetValue(changedPath, out var exact))
                {
                    foreach (var cb in exact.ToArray())
                        cb(new StatPath(changedPath), _values.TryGetValue(changedPath, out var v) ? v : 0);
                }
            }

            foreach (var changedPath in changed)
            {
                foreach (var kvp in _subs)
                {
                    if (kvp.Key.Contains('*'))
                    {
                        var pattern = new StatPath(kvp.Key);
                        if (pattern.Matches(new StatPath(changedPath)))
                        {
                            foreach (var cb in kvp.Value.ToArray())
                                cb(new StatPath(changedPath), GetAggregate(kvp.Key));
                        }
                    }
                }
            }
        }

        public void SetAggregate(StatPath pattern, long value)
        {
            _values[pattern.Value] = value;
        }

        public void Subscribe(StatPath pattern, Action<StatPath, long> callback)
        {
            if (!_subs.TryGetValue(pattern.Value, out var list))
            {
                list = new List<Action<StatPath, long>>();
                _subs[pattern.Value] = list;
            }
            list.Add(callback);
        }

        public void Unsubscribe(StatPath pattern, Action<StatPath, long> callback)
        {
            if (_subs.TryGetValue(pattern.Value, out var list))
                list.Remove(callback);
        }

        public void UnsubscribeAll()
        {
            _subs.Clear();
        }

        public long GetAggregate(StatPath pattern)
        {
            if (!pattern.IsPattern)
                return _values.TryGetValue(pattern.Value, out var v) ? v : 0;

            long total = 0;
            foreach (var kvp in _values)
            {
                if (pattern.Matches(new StatPath(kvp.Key)))
                    total += kvp.Value;
            }
            return total;
        }

    }
}
