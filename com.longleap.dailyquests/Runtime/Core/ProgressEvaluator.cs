using System;
using System.Collections.Generic;
using LongLeap.Stats;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Progress of an active quest from stats: baselines at issue time, objective
    /// progress, completion latches. The state (<see cref="ActiveQuestState"/>) is changed
    /// only by <see cref="CreateEntry"/>, <see cref="EnsureBaselines"/> and
    /// <see cref="Evaluate"/>; <see cref="Compute"/> only reads.
    /// </summary>
    internal sealed class ProgressEvaluator
    {
        private readonly IStatSystem _stats;
        private readonly Dictionary<string, StatPath> _paths = new(StringComparer.Ordinal);

        /// <param name="stats"><c>null</c> — every stat is 0, no progress.</param>
        public ProgressEvaluator(IStatSystem stats)
        {
            _stats = stats;
        }

        public StatPath PathOf(string raw)
        {
            if (!_paths.TryGetValue(raw, out var path))
            {
                path = new StatPath(raw);
                _paths[raw] = path;
            }
            return path;
        }

        public long ReadStat(string path) => _stats?.GetAggregate(PathOf(path)) ?? 0;

        /// <summary>A new entry for an issued quest: the baseline is the current stats.</summary>
        public ActiveQuestState CreateEntry(QuestDefinition def)
        {
            var baselines = new ObjectiveBaseline[def.Objectives.Length];
            for (int i = 0; i < baselines.Length; i++)
            {
                var obj = def.Objectives[i];
                baselines[i] = new ObjectiveBaseline { ObjectiveId = obj.Id.Value, Value = ReadStat(obj.StatPath) };
            }

            return new ActiveQuestState { QuestId = def.Id.Value, Baselines = baselines };
        }

        /// <summary>
        /// An objective was added to the JSON mid-period — count it from this moment.
        /// true — baselines were added, the state must be saved.
        /// </summary>
        public bool EnsureBaselines(ActiveQuestState entry, QuestDefinition def)
        {
            var baselines = new List<ObjectiveBaseline>();
            if (entry.Baselines != null)
            {
                foreach (var b in entry.Baselines)
                    if (b != null) baselines.Add(b);
            }

            bool added = false;
            foreach (var obj in def.Objectives)
            {
                if (baselines.Exists(b => b.ObjectiveId == obj.Id.Value)) continue;

                baselines.Add(new ObjectiveBaseline { ObjectiveId = obj.Id.Value, Value = ReadStat(obj.StatPath) });
                added = true;
            }

            entry.Baselines = baselines.ToArray();
            return added;
        }

        /// <summary>Objective progress right now. Changes nothing.</summary>
        public QuestProgress Compute(ActiveQuestState entry, QuestObjective obj)
        {
            var baseline = BaselineOf(entry, obj);

            if (entry.IsCompleted || baseline is { IsCompleted: true })
                return new QuestProgress(obj.StatTarget, obj.StatTarget);

            return Progress(obj, baseline, ReadStat(obj.StatPath));
        }

        /// <summary>
        /// Check a quest against stats: latch met objectives and, once all are
        /// met, the quest itself (<paramref name="completed"/>). Also lowers the
        /// baseline of a Delta objective if the stat dropped below it.
        /// true — the state changed and must be saved.
        /// </summary>
        public bool Evaluate(ActiveQuestState entry, QuestDefinition def, out bool completed)
        {
            completed = false;
            if (entry.IsCompleted) return false;

            bool changed = false;
            bool allDone = true;

            foreach (var obj in def.Objectives)
            {
                var baseline = BaselineOf(entry, obj);
                if (baseline is { IsCompleted: true }) continue;

                long value = ReadStat(obj.StatPath);

                if (obj.Mode == QuestProgressMode.Delta && baseline != null && value < baseline.Value)
                {
                    // The stat dropped below the baseline: the stats save turned out older than
                    // the quests save, or stats were reset. Without this progress would sit at
                    // zero until the stat caught up with the old mark.
                    baseline.Value = value;
                    changed = true;
                }

                if (!Progress(obj, baseline, value).IsComplete)
                {
                    allDone = false;
                    continue;
                }

                if (baseline != null)
                {
                    baseline.IsCompleted = true;
                    changed = true;
                }
            }

            if (!allDone) return changed;

            entry.IsCompleted = true;
            completed = true;
            return true;
        }

        /// <summary>The objective is latched — its stat no longer affects progress.</summary>
        public static bool IsLatched(ActiveQuestState entry, QuestObjective obj) =>
            entry.IsCompleted || BaselineOf(entry, obj) is { IsCompleted: true };

        private static QuestProgress Progress(QuestObjective obj, ObjectiveBaseline baseline, long value)
        {
            if (obj.Mode == QuestProgressMode.Delta)
                value -= baseline?.Value ?? value;

            // Clamp to the target: nobody needs "57/50" in the UI.
            value = Math.Max(0, Math.Min(value, obj.StatTarget));
            return new QuestProgress((int)value, obj.StatTarget);
        }

        private static ObjectiveBaseline BaselineOf(ActiveQuestState entry, QuestObjective obj)
        {
            if (entry.Baselines == null) return null;

            foreach (var b in entry.Baselines)
                if (b != null && b.ObjectiveId == obj.Id.Value) return b;

            return null;
        }
    }
}
