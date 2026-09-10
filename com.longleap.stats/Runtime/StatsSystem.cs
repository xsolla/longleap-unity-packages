using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.Stats
{
    public sealed class StatsSystem : IStatSystem
    {
        private readonly Dictionary<string, StatNode> _nodes = new();
        private readonly Dictionary<string, List<Action<StatPath, long>>> _subscriptions = new();
        private readonly HashSet<string> _dirtyLeaves = new();
        private readonly object _lock = new();

        private CancellationTokenSource _autoSaveCts;
        private IStatPersistence _persistence;
        private TimeSpan _autoSaveDebounce;
        private bool _autoSavePending;

        public void Define(StatPath path, StatType type)
        {
            lock (_lock)
            {
                if (_nodes.ContainsKey(path.Value))
                    return;

                if (type == StatType.Value || type == StatType.MaxValue)
                {
                    if (!path.Parent.IsNone && !_nodes.ContainsKey(path.Parent.Value))
                        throw new InvalidOperationException(
                            $"StatsSystem: parent '{path.Parent.Value}' not defined for leaf '{path.Value}'. Define parent first.");
                }
                else
                {
                    if (!path.IsNone && _nodes.ContainsKey(path.Value))
                    {
                        foreach (var kvp in _nodes)
                        {
                            if (kvp.Key.StartsWith(path.Value + ":"))
                                throw new InvalidOperationException(
                                    $"StatsSystem: cannot define non-leaf '{path.Value}' as {type} because children already exist.");
                        }
                    }

                    if (!path.Parent.IsNone && !_nodes.ContainsKey(path.Parent.Value))
                        throw new InvalidOperationException(
                            $"StatsSystem: parent '{path.Parent.Value}' not defined for non-leaf '{path.Value}'. Define parent first.");
                }

                _nodes[path.Value] = new StatNode(type);
            }
        }

        public bool IsDefined(StatPath path)
        {
            lock (_lock)
            {
                return !path.IsNone && _nodes.ContainsKey(path.Value);
            }
        }

        public void Set(StatPath path, long value)
        {
            lock (_lock)
            {
                if (!_nodes.TryGetValue(path.Value, out var node))
                    throw new InvalidOperationException($"StatsSystem: path '{path.Value}' is not defined.");

                if (node.Type == StatType.Accumulate || node.Type == StatType.Max)
                    throw new InvalidOperationException($"StatsSystem: cannot Set on non-leaf path '{path.Value}' of type {node.Type}.");

                long newValue;
                switch (node.Type)
                {
                    case StatType.Value:
                        newValue = value;
                        break;
                    case StatType.MaxValue:
                        newValue = Math.Max(node.Value, value);
                        break;
                    default:
                        throw new InvalidOperationException($"StatsSystem: unexpected leaf type {node.Type}.");
                }

                if (newValue == node.Value) return;

                node.Value = newValue;
                _dirtyLeaves.Add(path.Value);
                FireChanged(path, newValue);
                CascadeUp(path.Parent);
                ScheduleAutoSave();
            }
        }

        public void Add(StatPath path, long delta)
        {
            lock (_lock)
            {
                if (!_nodes.TryGetValue(path.Value, out var node))
                    throw new InvalidOperationException($"StatsSystem: path '{path.Value}' is not defined.");

                if (node.Type != StatType.Value)
                    throw new InvalidOperationException($"StatsSystem: Add only valid for Value-type leaf, '{path.Value}' is {node.Type}.");

                if (delta == 0) return;

                node.Value += delta;
                _dirtyLeaves.Add(path.Value);
                FireChanged(path, node.Value);
                CascadeUp(path.Parent);
                ScheduleAutoSave();
            }
        }

        private void CascadeUp(StatPath path)
        {
            if (path.IsNone) return;
            if (!_nodes.TryGetValue(path.Value, out var node)) return;

            long newValue = node.Type switch
            {
                StatType.Accumulate => SumDirectChildren(path),
                StatType.Max => MaxDirectChildren(path),
                _ => node.Value
            };

            if (newValue == node.Value) return;

            node.Value = newValue;
            FireChanged(path, newValue);
            CascadeUp(path.Parent);
        }

        private long AggregateDirectChildren(StatPath path, long seed, Func<long, long, long> aggregate)
        {
            long result = seed;
            string prefix = path.Value + ":";
            foreach (var kvp in _nodes)
            {
                if (kvp.Key.Length > prefix.Length && kvp.Key.StartsWith(prefix))
                {
                    string remainder = kvp.Key.Substring(prefix.Length);
                    if (!remainder.Contains(':'))
                        result = aggregate(result, kvp.Value.Value);
                }
            }
            return result;
        }

        private long SumDirectChildren(StatPath path) => AggregateDirectChildren(path, 0, (a, b) => a + b);
        private long MaxDirectChildren(StatPath path) => AggregateDirectChildren(path, long.MinValue, Math.Max);

        private void FireChanged(StatPath path, long value)
        {
            if (_subscriptions.TryGetValue(path.Value, out var subs))
            {
                foreach (var sub in subs.ToArray())
                {
                    try { sub(path, value); }
                    catch (Exception e) { Debug.LogError($"StatsSystem: subscriber error for '{path.Value}': {e.Message}"); }
                }
            }

            foreach (var kvp in _subscriptions.ToArray())
            {
                if (kvp.Key.Contains('*'))
                {
                    var pattern = new StatPath(kvp.Key);
                    if (pattern.Matches(path))
                    {
                        foreach (var sub in kvp.Value.ToArray())
                        {
                            try { sub(path, value); }
                            catch (Exception e) { Debug.LogError($"StatsSystem: subscriber error for '{path.Value}': {e.Message}"); }
                        }
                    }
                }
            }
        }

        public long Get(StatPath path)
        {
            lock (_lock)
            {
                return _nodes.TryGetValue(path.Value, out var node) ? node.Value : 0;
            }
        }

        public void Subscribe(StatPath pattern, Action<StatPath, long> callback)
        {
            lock (_lock)
            {
                if (!_subscriptions.TryGetValue(pattern.Value, out var list))
                {
                    list = new List<Action<StatPath, long>>();
                    _subscriptions[pattern.Value] = list;
                }
                list.Add(callback);
            }
        }

        public void Unsubscribe(StatPath pattern, Action<StatPath, long> callback)
        {
            lock (_lock)
            {
                if (_subscriptions.TryGetValue(pattern.Value, out var list))
                    list.Remove(callback);
            }
        }

        public void UnsubscribeAll()
        {
            lock (_lock)
            {
                _subscriptions.Clear();
            }
        }

        public long GetAggregate(StatPath pattern)
        {
            lock (_lock)
            {
                if (!pattern.IsPattern)
                    return _nodes.TryGetValue(pattern.Value, out var node) ? node.Value : 0;

                var op = DetermineAggregateOp(pattern);
                long result = op == StatType.Max ? long.MinValue : 0;
                bool hasAny = false;

                foreach (var kvp in _nodes)
                {
                    if (pattern.Matches(new StatPath(kvp.Key)))
                    {
                        hasAny = true;
                        if (op == StatType.Accumulate)
                            result += kvp.Value.Value;
                        else if (op == StatType.Max)
                            result = Math.Max(result, kvp.Value.Value);
                    }
                }

                return hasAny ? result : 0;
            }
        }

        private StatType DetermineAggregateOp(StatPath pattern)
        {
            var segments = pattern.Value.Split(':');
            for (int len = segments.Length - 1; len >= 1; len--)
            {
                bool hasWildcard = false;
                for (int i = 0; i < len; i++)
                {
                    if (segments[i] == "*") { hasWildcard = true; break; }
                }
                if (hasWildcard) continue;

                var candidate = string.Join(":", segments, 0, len);
                if (_nodes.TryGetValue(candidate, out var node))
                    return node.Type;
            }
            return StatType.Accumulate;
        }

        public async Task SaveAsync(IStatPersistence persistence)
        {
            List<(StatPath path, long value)> dirty;
            lock (_lock)
            {
                dirty = new List<(StatPath, long)>(_dirtyLeaves.Count);
                foreach (var leafKey in _dirtyLeaves)
                {
                    if (_nodes.TryGetValue(leafKey, out var node)
                        && (node.Type == StatType.Value || node.Type == StatType.MaxValue))
                    {
                        dirty.Add((new StatPath(leafKey), node.Value));
                    }
                }
                _dirtyLeaves.Clear();
            }

            foreach (var entry in dirty)
                await persistence.SaveAsync(entry.path, entry.value);
        }

        public async Task LoadAsync(IStatPersistence persistence)
        {
            List<string> leafKeys;
            lock (_lock)
            {
                leafKeys = new List<string>();
                foreach (var kvp in _nodes)
                {
                    if (kvp.Value.Type == StatType.Value || kvp.Value.Type == StatType.MaxValue)
                    {
                        kvp.Value.Value = 0;
                        leafKeys.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in leafKeys)
            {
                var result = await persistence.LoadAsync(new StatPath(key));
                if (!result.Exists)
                    continue;

                lock (_lock)
                {
                    if (_nodes.TryGetValue(key, out var node)
                        && (node.Type == StatType.Value || node.Type == StatType.MaxValue))
                    {
                        node.Value = result.Value;
                    }
                }
            }

            lock (_lock)
            {
                var ordered = _nodes.Keys
                    .Where(k => _nodes[k].Type == StatType.Accumulate || _nodes[k].Type == StatType.Max)
                    .OrderByDescending(k => k.Count(c => c == ':'));
                foreach (var key in ordered)
                {
                    var path = new StatPath(key);
                    var node = _nodes[key];
                    node.Value = node.Type == StatType.Accumulate
                        ? SumDirectChildren(path) : MaxDirectChildren(path);
                }
            }
        }

        public void StartAutoSave(IStatPersistence persistence, TimeSpan debounce)
        {
            _persistence = persistence;
            _autoSaveDebounce = debounce;
            _autoSaveCts?.Cancel();
            _autoSaveCts?.Dispose();
            _autoSaveCts = new CancellationTokenSource();
        }

        private void ScheduleAutoSave()
        {
            if (_persistence == null || _autoSavePending) return;
            _autoSavePending = true;
            var _ = AutoSaveAsync();
        }

        private async Task AutoSaveAsync()
        {
            try
            {
                await Task.Delay(_autoSaveDebounce, _autoSaveCts?.Token ?? default);
                _autoSavePending = false;
                await SaveAsync(_persistence);
            }
            catch (OperationCanceledException)
            {
                _autoSavePending = false;
            }
            catch (Exception e)
            {
                _autoSavePending = false;
                Debug.LogError($"StatsSystem: ошибка автосохранения: {e.Message}");
            }
        }

        public void StopAutoSave()
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts?.Dispose();
            _autoSaveCts = null;
        }

        public async Task FlushAsync()
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts?.Dispose();
            _autoSaveCts = _persistence != null ? new CancellationTokenSource() : null;
            _autoSavePending = false;
            if (_persistence != null)
                await SaveAsync(_persistence);
        }

        private sealed class StatNode
        {
            public readonly StatType Type;
            public long Value;

            public StatNode(StatType type)
            {
                Type = type;
                Value = 0;
            }
        }

    }
}
