using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.Stats;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Daily quests: a set is issued from the pool for each period (from reset to
    /// reset, UTC), progress is derived from stats, the player claims rewards by hand.
    ///
    /// Quest lifecycle: InProgress → Completed (objectives met, the reward is
    /// waiting) → Claimed (the player pressed "claim", the game granted the reward).
    /// A reward not claimed before the reset is lost together with the set.
    ///
    /// The period changes lazily: there is no timer, every access checks it
    /// (GetActive, ClaimAsync, stat change, Tick). A game left open past
    /// midnight gets the new set the first time it looks at the quests; an open
    /// quests UI calls Tick so the set changes right in front of the player.
    ///
    /// A stat change is a hot path (every grab, every meter): only quests that
    /// watch this stat are checked, without allocations, and
    /// <see cref="QuestStatus"/> is rebuilt lazily — when someone reads it.
    /// </summary>
    public sealed class DailyQuestService : IDailyQuestService, IDailyQuestDebug
    {
        private readonly IDailyQuestProvider _provider;
        private readonly IDailyQuestStateStore _store;
        private readonly IDailyQuestGameBridge _bridge;
        private readonly IStatSystem _stats;
        private readonly Func<DateTime> _utcNow;
        private readonly ProgressEvaluator _progress;

        private readonly Dictionary<QuestId, QuestDefinition> _definitions = new();
        private readonly List<QuestDefinition> _pool = new();
        private readonly List<(StatPath Path, Action<StatPath, long> Callback)> _subscriptions = new();

        /// <summary>The set in memory — in the same order as <see cref="DailyQuestsState.Active"/>.</summary>
        private readonly List<ActiveQuest> _active = new();
        private QuestStatus[] _snapshot = Array.Empty<QuestStatus>();
        private bool _snapshotStale;

        private DailyQuestSettings _settings = new();
        private ResetSchedule _schedule = new(0);
        private DailyQuestsState _state;
        private bool _ready;
        private Task<bool> _saving = Task.FromResult(true);

        public event Action Changed;

        /// <param name="bridge">Required: the game grants rewards through it.</param>
        /// <param name="stats">Without stats quests make no progress — only via <see cref="IDailyQuestDebug.ForceComplete"/>.</param>
        /// <param name="utcNow">Clock. Defaults to <see cref="DateTime.UtcNow"/>; replaced in tests.</param>
        public DailyQuestService(
            IDailyQuestProvider provider,
            IDailyQuestStateStore store,
            IDailyQuestGameBridge bridge,
            IStatSystem stats = null,
            Func<DateTime> utcNow = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _stats = stats;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _progress = new ProgressEvaluator(stats);
        }

        public async Task InitializeAsync()
        {
            Shutdown();

            _definitions.Clear();
            _pool.Clear();
            _active.Clear();
            _snapshot = Array.Empty<QuestStatus>();
            _snapshotStale = false;

            var config = await _provider.LoadAsync() ?? new DailyQuestConfig();
            LogValidation(DailyQuestValidator.Validate(config));

            _settings = config.Settings ?? new DailyQuestSettings();
            _schedule = new ResetSchedule(_settings.ResetHourUtc);

            foreach (var def in config.Quests ?? Array.Empty<QuestDefinition>())
            {
                if (!DailyQuestValidator.IsPlayable(def)) continue;
                if (_definitions.ContainsKey(def.Id)) continue;

                _definitions[def.Id] = def;
                _pool.Add(def);
            }

            var loaded = await _store.LoadAsync();
            DailyQuestsState state;
            bool unreadable = false;

            switch (loaded.Status)
            {
                case DailyQuestStateLoadStatus.Loaded:
                    state = loaded.State;
                    break;

                case DailyQuestStateLoadStatus.Empty:
                    state = DailyQuestsState.Empty();
                    break;

                case DailyQuestStateLoadStatus.Unreadable:
                    // A save exists but cannot be read. What has already been claimed from
                    // today's set is unknown, and the pick is deterministic: if we issued the
                    // set again it would most likely match the old one, and rewards would
                    // be claimed twice. So there are no quests until the next reset.
                    Debug.LogWarning("DailyQuestService: save cannot be read — a new set will be issued after the next reset.");
                    unreadable = true;
                    state = new DailyQuestsState
                    {
                        PeriodStartUtcTicks = _schedule.PeriodStart(_utcNow()).Ticks,
                        Withheld = true
                    };
                    break;

                default:
                    // The store did not answer. Starting from scratch is not an option: an
                    // already claimed quest would be issued again and claimed twice.
                    Debug.LogError("DailyQuestService: state could not be loaded — quests are disabled until restart.");
                    return;
            }

            _state = state;
            _state.Active ??= Array.Empty<ActiveQuestState>();

            WarnUndefinedStats();
            Subscribe();
            _ready = true;

            if (!EnsureCurrentPeriod())
            {
                bool dirty = unreadable;
                dirty |= ReconcileActive();
                SyncActive();
                dirty |= EvaluateActive(default, notify: true, out _);
                if (dirty) await Save();
            }
        }

        public void Shutdown()
        {
            if (_stats != null)
            {
                foreach (var (path, callback) in _subscriptions)
                    _stats.Unsubscribe(path, callback);
            }

            _subscriptions.Clear();
            _ready = false;
        }

        public QuestStatus[] GetActive()
        {
            if (!_ready) return Array.Empty<QuestStatus>();

            EnsureCurrentPeriod();

            // A stat may have changed without an event — check progress on read.
            // With no changes this is a few stat reads and zero allocations.
            foreach (var quest in _active)
                Refresh(quest);

            return Snapshot();
        }

        public QuestStatus GetStatus(QuestId questId)
        {
            if (!_ready) return null;

            EnsureCurrentPeriod();

            var quest = Find(questId);
            if (quest == null) return null;

            Refresh(quest);
            return StatusOf(quest);
        }

        public int ClaimableCount
        {
            get
            {
                if (!_ready) return 0;

                EnsureCurrentPeriod();

                int count = 0;
                foreach (var quest in _active)
                    if (quest.State.IsCompleted && !quest.State.IsClaimed) count++;
                return count;
            }
        }

        public TimeSpan TimeUntilReset => _schedule.TimeUntilReset(_utcNow());

        public void Tick() => EnsureCurrentPeriod();

        public async Task<bool> ClaimAsync(QuestId questId)
        {
            if (!_ready) return false;

            EnsureCurrentPeriod();

            var quest = Find(questId);
            if (quest == null || quest.State.IsClaimed) return false;

            if (!quest.State.IsCompleted)
            {
                // A stat may have grown without an event (stats came up after us) — re-check.
                if (EvaluateActive(default, notify: true, out bool changed)) _ = Save();

                if (!quest.State.IsCompleted)
                {
                    // This quest is not complete, but others may have latched — the UI must know.
                    if (changed) RaiseChanged();
                    return false;
                }
            }

            var def = quest.Definition;

            // Mark as claimed and wait for the write first, then grant:
            // if the game crashes between these steps the player loses one reward
            // but never gets it twice. The flag is set before the await, so a second
            // press while the write is in flight gets false.
            quest.State.IsClaimed = true;
            if (!await Save())
            {
                // The mark was not written — do not grant: after a restart the quest
                // would show as unclaimed and the reward would be claimed again.
                quest.State.IsClaimed = false;
                return false;
            }

            try
            {
                _bridge.ApplyRewards(new QuestClaimResult
                {
                    Quest = def,
                    Rewards = def.Rewards ?? Array.Empty<QuestReward>()
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"DailyQuestService: ApplyRewards failed for '{questId}': {e}");
            }

            Refresh(quest);
            RaiseChanged();
            return true;
        }

        // --- Debug -------------------------------------------------------------

        void IDailyQuestDebug.ForceComplete(QuestId questId)
        {
            if (!_ready) return;

            EnsureCurrentPeriod();

            var quest = Find(questId);
            if (quest == null || quest.State.IsCompleted) return;

            quest.State.IsCompleted = true;
            Refresh(quest);
            Save();
            NotifyCompleted(quest);
            RaiseChanged();
        }

        void IDailyQuestDebug.SkipToNextPeriod()
        {
            if (!_ready) return;
            Rotate(_schedule.PeriodStart(_utcNow()).Ticks);
        }

        void IDailyQuestDebug.ResetState()
        {
            if (!_ready) return;

            _state = DailyQuestsState.Empty();
            EnsureCurrentPeriod();
        }

        // --- Period ------------------------------------------------------------

        /// <summary>Issue a new set if the period has changed. true — it has.</summary>
        private bool EnsureCurrentPeriod()
        {
            if (!_ready) return false;

            var periodStart = _schedule.PeriodStart(_utcNow()).Ticks;

            // Same period — or the clock was moved back: leave the set alone, otherwise
            // moving the clock back and forth would let players reroll quests.
            if (_state.PeriodStartUtcTicks >= periodStart) return false;

            Rotate(periodStart);
            return true;
        }

        private void Rotate(long periodStartTicks)
        {
            var previous = ActiveIds(_state.Active);

            var picked = DailyQuestSelector.Pick(
                _pool,
                _settings.DailyCount,
                Seed(periodStartTicks),
                avoid: previous,
                settings: _settings);

            _state.PeriodStartUtcTicks = periodStartTicks;
            _state.Withheld = false;
            _state.Active = picked.Select(_progress.CreateEntry).ToArray();
            SyncActive();

            // An Absolute objective may be met right away. No separate event is sent:
            // such a quest arrives already completed in OnQuestsRotated.
            EvaluateActive(default, notify: false, out _);
            Save();

            try { _bridge.OnQuestsRotated(Snapshot()); }
            catch (Exception e) { Debug.LogError($"DailyQuestService: OnQuestsRotated failed: {e}"); }

            RaiseChanged();
        }

        /// <summary>
        /// Reconcile the saved set with the current config (within a period):
        /// quests removed from the JSON drop out, new objectives get a baseline,
        /// and if DailyCount grew or quests dropped out, the set is refilled.
        /// </summary>
        private bool ReconcileActive()
        {
            bool dirty = false;
            var kept = new List<ActiveQuestState>();
            var keptDefs = new List<QuestDefinition>();

            foreach (var entry in _state.Active)
            {
                if (entry == null || string.IsNullOrEmpty(entry.QuestId) ||
                    !_definitions.TryGetValue(new QuestId(entry.QuestId), out var def) ||
                    keptDefs.Contains(def))
                {
                    dirty = true;
                    continue;
                }

                dirty |= _progress.EnsureBaselines(entry, def);
                kept.Add(entry);
                keptDefs.Add(def);
            }

            // The set for this period is withheld on purpose — nothing to refill.
            int missing = _state.Withheld ? 0 : _settings.DailyCount - kept.Count;
            if (missing > 0)
            {
                var extra = DailyQuestSelector.Pick(
                    _pool,
                    missing,
                    Seed(_state.PeriodStartUtcTicks) + kept.Count,
                    settings: _settings,
                    selected: keptDefs);

                foreach (var def in extra)
                {
                    kept.Add(_progress.CreateEntry(def));
                    keptDefs.Add(def);
                }

                dirty |= extra.Count > 0;
            }

            if (_settings.HasSelectionRules)
            {
                // With rules the set goes from easy to hard — after a refill too.
                var ordered = kept
                    .Select((entry, i) => (entry, def: keptDefs[i]))
                    .OrderBy(x => x.def.Difficulty)
                    .Select(x => x.entry)
                    .ToList();

                dirty |= !ordered.SequenceEqual(kept);
                kept = ordered;
            }

            _state.Active = kept.ToArray();
            return dirty;
        }

        private static int Seed(long periodStartTicks) =>
            (periodStartTicks / TimeSpan.TicksPerHour).GetHashCode();

        // --- Set in memory -----------------------------------------------------

        /// <summary>An active quest: definition, persisted state and what the UI has seen.</summary>
        private sealed class ActiveQuest
        {
            public readonly QuestDefinition Definition;
            public readonly ActiveQuestState State;

            /// <summary>Last computed progress per objective — compared against to detect changes.</summary>
            public readonly QuestProgress[] Progress;
            public QuestState Shown = (QuestState)(-1);

            /// <summary>Snapshot for the UI; <c>null</c> — stale, rebuild on read.</summary>
            public QuestStatus Status;

            public ActiveQuest(QuestDefinition definition, ActiveQuestState state)
            {
                Definition = definition;
                State = state;
                Progress = new QuestProgress[definition.Objectives.Length];
            }
        }

        /// <summary>Rebuild the in-memory set from <see cref="DailyQuestsState.Active"/>.</summary>
        private void SyncActive()
        {
            _active.Clear();

            foreach (var entry in _state.Active)
            {
                if (entry == null || string.IsNullOrEmpty(entry.QuestId)) continue;
                if (!_definitions.TryGetValue(new QuestId(entry.QuestId), out var def)) continue;

                var quest = new ActiveQuest(def, entry);
                Refresh(quest);
                _active.Add(quest);
            }

            _snapshotStale = true;
        }

        /// <summary>
        /// Recompute the visible progress and state of a quest. true — some of it
        /// changed (the snapshot is stale). Allocates nothing when nothing changed.
        /// </summary>
        private bool Refresh(ActiveQuest quest)
        {
            bool changed = false;

            var shown = StateOf(quest.State);
            if (shown != quest.Shown)
            {
                quest.Shown = shown;
                changed = true;
            }

            var objectives = quest.Definition.Objectives;
            for (int i = 0; i < objectives.Length; i++)
            {
                var progress = _progress.Compute(quest.State, objectives[i]);
                if (progress == quest.Progress[i]) continue;

                quest.Progress[i] = progress;
                changed = true;
            }

            if (changed)
            {
                quest.Status = null;
                _snapshotStale = true;
            }

            return changed;
        }

        private QuestStatus StatusOf(ActiveQuest quest)
        {
            if (quest.Status != null) return quest.Status;

            var objectives = quest.Definition.Objectives;
            var progresses = new QuestObjectiveProgress[objectives.Length];
            for (int i = 0; i < objectives.Length; i++)
                progresses[i] = new QuestObjectiveProgress { ObjectiveId = objectives[i].Id, Progress = quest.Progress[i] };

            return quest.Status = new QuestStatus
            {
                Definition = quest.Definition,
                State = quest.Shown,
                ObjectiveProgresses = progresses
            };
        }

        private QuestStatus[] Snapshot()
        {
            if (!_snapshotStale) return _snapshot;

            var snapshot = new QuestStatus[_active.Count];
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = StatusOf(_active[i]);

            _snapshot = snapshot;
            _snapshotStale = false;
            return snapshot;
        }

        private static QuestState StateOf(ActiveQuestState entry) =>
            entry.IsClaimed ? QuestState.Claimed
            : entry.IsCompleted ? QuestState.Completed
            : QuestState.InProgress;

        // --- Progress ----------------------------------------------------------

        /// <summary>
        /// Check active quests against stats (all of them, or only those watching
        /// <paramref name="onlyPath"/>): latch what is met, update the visible
        /// progress. true — the state must be saved; <paramref name="changed"/> —
        /// something the UI shows has changed.
        /// </summary>
        private bool EvaluateActive(StatPath onlyPath, bool notify, out bool changed)
        {
            bool dirty = false;
            changed = false;
            List<ActiveQuest> completed = null;

            foreach (var quest in _active)
            {
                if (!onlyPath.IsNone && !Uses(quest, onlyPath)) continue;

                dirty |= _progress.Evaluate(quest.State, quest.Definition, out bool done);
                changed |= Refresh(quest);

                if (done && notify)
                    (completed ??= new List<ActiveQuest>()).Add(quest);
            }

            if (completed != null)
            {
                foreach (var quest in completed)
                    NotifyCompleted(quest);
            }

            return dirty;
        }

        /// <summary>The quest is still waiting for progress on this stat.</summary>
        private bool Uses(ActiveQuest quest, StatPath path)
        {
            if (quest.State.IsCompleted) return false;

            foreach (var obj in quest.Definition.Objectives)
            {
                if (_progress.PathOf(obj.StatPath) == path && !ProgressEvaluator.IsLatched(quest.State, obj))
                    return true;
            }

            return false;
        }

        // --- Stats -------------------------------------------------------------

        private void Subscribe()
        {
            if (_stats == null) return;

            var paths = new HashSet<StatPath>();
            foreach (var def in _pool)
            foreach (var obj in def.Objectives)
                paths.Add(_progress.PathOf(obj.StatPath));

            foreach (var path in paths)
            {
                var subscribed = path;
                Action<StatPath, long> callback = (_, _) => OnStatChanged(subscribed);
                _stats.Subscribe(subscribed, callback);
                _subscriptions.Add((subscribed, callback));
            }
        }

        private void OnStatChanged(StatPath path)
        {
            if (!_ready) return;

            // The period changed just now — Rotate has already recomputed and notified.
            if (EnsureCurrentPeriod()) return;

            if (EvaluateActive(path, notify: true, out bool changed))
                Save();

            if (changed)
                RaiseChanged();
        }

        private void WarnUndefinedStats()
        {
            if (_stats == null)
            {
                Debug.LogWarning("DailyQuestService: no stats — quests will not make progress.");
                return;
            }

            // A typo in statPath does not break the build — the quest just never
            // completes. Catch it at least with a warning in the log.
            foreach (var def in _pool)
            {
                foreach (var obj in def.Objectives)
                {
                    var path = _progress.PathOf(obj.StatPath);
                    if (!path.IsPattern && !_stats.IsDefined(path))
                        Debug.LogWarning($"DailyQuestService: quest '{def.Id}' watches stat '{path}', which is not defined — it will never complete.");
                }
            }
        }

        // --- Helpers -----------------------------------------------------------

        private ActiveQuest Find(QuestId questId)
        {
            if (questId.IsNone) return null;

            foreach (var quest in _active)
                if (quest.Definition.Id == questId) return quest;

            return null;
        }

        private static List<QuestId> ActiveIds(IEnumerable<ActiveQuestState> entries) =>
            entries
                .Where(e => e != null && !string.IsNullOrEmpty(e.QuestId))
                .Select(e => new QuestId(e.QuestId))
                .ToList();

        private void NotifyCompleted(ActiveQuest quest)
        {
            try { _bridge.OnQuestCompleted(StatusOf(quest)); }
            catch (Exception e) { Debug.LogError($"DailyQuestService: OnQuestCompleted failed for '{quest.Definition.Id}': {e}"); }
        }

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogError($"DailyQuestService: a Changed subscriber failed: {e}"); }
        }

        private static void LogValidation(DailyQuestValidationResult result)
        {
            foreach (var error in result.Errors)
                Debug.LogError($"DailyQuestService: {error}");
            foreach (var warning in result.Warnings)
                Debug.LogWarning($"DailyQuestService: {warning}");
        }

        /// <summary>
        /// Write the state. Writes go strictly one after another — each waits for
        /// the previous one, otherwise an old state could land on top of a new one.
        /// What is written is the in-memory state at write time, not at call time.
        /// false — the write failed (the error is already logged).
        /// </summary>
        private Task<bool> Save() => _saving = SaveAfter(_saving);

        private async Task<bool> SaveAfter(Task previous)
        {
            await previous;

            try
            {
                await _store.SaveAsync(_state);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"DailyQuestService: failed to save state: {e.Message}");
                return false;
            }
        }
    }
}
