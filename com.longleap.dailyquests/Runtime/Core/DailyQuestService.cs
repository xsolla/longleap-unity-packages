using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.Stats;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Ежедневные квесты: из пула выдаётся набор на период (от сброса до сброса
    /// по UTC), прогресс выводится из статов, награду игрок забирает сам.
    ///
    /// Жизненный цикл квеста: InProgress → Completed (цели набраны, награда
    /// ждёт) → Claimed (игрок нажал «забрать», игра начислила награду).
    /// Невыбранная до сброса награда сгорает вместе с набором.
    ///
    /// Смена периода ленивая: таймера нет, проверка идёт при каждом обращении
    /// (GetActive, ClaimAsync, изменение стата, Tick). Игра, открытая через
    /// полночь, получит новый набор при первом же взгляде на квесты; открытый
    /// UI квестов зовёт Tick, чтобы набор сменился у игрока на глазах.
    /// </summary>
    public sealed class DailyQuestService : IDailyQuestService
    {
        private readonly IDailyQuestProvider _provider;
        private readonly IDailyQuestStateStore _store;
        private readonly IDailyQuestGameBridge _bridge;
        private readonly IStatSystem _stats;
        private readonly Func<DateTime> _utcNow;

        private readonly Dictionary<QuestId, QuestDefinition> _definitions = new();
        private readonly List<QuestDefinition> _pool = new();
        private readonly Dictionary<string, StatPath> _paths = new(StringComparer.Ordinal);
        private readonly List<(StatPath Path, Action<StatPath, long> Callback)> _subscriptions = new();

        private DailyQuestSettings _settings = new();
        private DailyQuestsState _state;
        private bool _ready;
        private Task<bool> _saving = Task.FromResult(true);

        public event Action Changed;

        /// <param name="stats">Без статов квесты не набирают прогресс — только ForceComplete.</param>
        /// <param name="utcNow">Часы. По умолчанию <see cref="DateTime.UtcNow"/>; подменяются в тестах.</param>
        public DailyQuestService(
            IDailyQuestProvider provider,
            IDailyQuestStateStore store,
            IDailyQuestGameBridge bridge = null,
            IStatSystem stats = null,
            Func<DateTime> utcNow = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bridge = bridge;
            _stats = stats;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public async Task InitializeAsync()
        {
            Shutdown();

            _definitions.Clear();
            _pool.Clear();
            _paths.Clear();

            var config = await _provider.LoadAsync() ?? new DailyQuestConfig();
            LogValidation(DailyQuestValidator.Validate(config));

            _settings = config.Settings ?? new DailyQuestSettings();

            foreach (var def in config.Quests ?? Array.Empty<QuestDefinition>())
            {
                if (!DailyQuestValidator.IsPlayable(def)) continue;
                if (_definitions.ContainsKey(def.Id)) continue;

                _definitions[def.Id] = def;
                _pool.Add(def);

                foreach (var obj in def.Objectives)
                    PathOf(obj.StatPath);
            }

            var state = await _store.LoadAsync();
            if (state == null)
            {
                // Хранилище не ответило. Начать с чистого листа нельзя: уже
                // забранный квест выдался бы снова, и награду взяли бы дважды.
                Debug.LogError("DailyQuestService: состояние не прочиталось — квесты выключены до перезапуска.");
                return;
            }

            bool unreadable = state.IsUnreadable;
            if (unreadable)
            {
                // Сейв есть, но не читается. Что из сегодняшнего набора уже
                // забрано — неизвестно, а выбор детерминирован: выдай мы набор
                // заново, он, скорее всего, совпал бы со старым, и награды
                // забрали бы второй раз. Поэтому до следующего сброса квестов нет.
                Debug.LogWarning("DailyQuestService: сохранение не читается — новый набор будет после ближайшего сброса.");
                state = new DailyQuestsState
                {
                    PeriodStartUtcTicks = LastResetUtc(_utcNow()).Ticks,
                    Withheld = true
                };
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
                dirty |= EvaluateCompletion(notify: true);
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
            return BuildActive();
        }

        public QuestStatus GetStatus(QuestId questId)
        {
            if (!_ready) return null;

            EnsureCurrentPeriod();
            var entry = Find(questId);
            return entry != null ? BuildStatus(entry) : null;
        }

        public int ClaimableCount => GetActive().Count(s => s.CanClaim);

        public TimeSpan TimeUntilReset
        {
            get
            {
                var now = _utcNow();
                return LastResetUtc(now).AddDays(1) - now;
            }
        }

        public void Tick() => EnsureCurrentPeriod();

        public async Task<bool> ClaimAsync(QuestId questId)
        {
            if (!_ready) return false;

            EnsureCurrentPeriod();

            var entry = Find(questId);
            if (entry == null || entry.IsClaimed) return false;

            if (!entry.IsCompleted)
            {
                // Стат мог вырасти без события (статы подняли позже нас) — сверяемся.
                bool latched = EvaluateCompletion(notify: true);
                if (latched) _ = Save();

                if (!entry.IsCompleted)
                {
                    // Этот квест не выполнен, но могли защёлкнуться другие — UI должен узнать.
                    if (latched) RaiseChanged();
                    return false;
                }
            }

            var def = _definitions[questId];

            // Сперва помечаем забранным и дожидаемся записи, потом начисляем:
            // упади игра между этими шагами — игрок потеряет одну награду, но
            // не получит её дважды. Флаг ставится до await, так что второе
            // нажатие, пока идёт запись, получит false.
            entry.IsClaimed = true;
            if (!await Save())
            {
                // Отметка не записалась — не начисляем: после перезапуска квест
                // оказался бы не забран, и награду взяли бы снова.
                entry.IsClaimed = false;
                return false;
            }

            try
            {
                _bridge?.ApplyRewards(new QuestClaimResult
                {
                    Quest = def,
                    Rewards = def.Rewards ?? Array.Empty<QuestReward>()
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"DailyQuestService: ошибка в ApplyRewards для '{questId}': {e}");
            }

            RaiseChanged();
            return true;
        }

        public void ForceComplete(QuestId questId)
        {
            if (!_ready) return;

            EnsureCurrentPeriod();

            var entry = Find(questId);
            if (entry == null || entry.IsCompleted) return;

            entry.IsCompleted = true;
            Save();
            NotifyCompleted(entry);
            RaiseChanged();
        }

        public void DebugSkipToNextResetWindow()
        {
            if (!_ready) return;
            Rotate(LastResetUtc(_utcNow()).Ticks);
        }

        public void Reset()
        {
            if (!_ready) return;

            _state = DailyQuestsState.Empty();
            EnsureCurrentPeriod();
        }

        // --- Период ---------------------------------------------------------

        /// <summary>Выдать новый набор, если период сменился. true — сменился.</summary>
        private bool EnsureCurrentPeriod()
        {
            if (!_ready) return false;

            var periodStart = LastResetUtc(_utcNow()).Ticks;

            // Тот же период — или часы перевели назад: набор не трогаем, иначе
            // переводом часов туда-обратно можно было бы крутить квесты.
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
                avoid: previous);

            _state.PeriodStartUtcTicks = periodStartTicks;
            _state.Withheld = false;
            _state.Active = picked.Select(CreateEntry).ToArray();

            // Absolute-цель может оказаться набранной сразу. Отдельного события
            // не шлём: такой квест придёт уже выполненным в OnQuestsRotated.
            EvaluateCompletion(notify: false);
            Save();

            try { _bridge?.OnQuestsRotated(BuildActive()); }
            catch (Exception e) { Debug.LogError($"DailyQuestService: ошибка в OnQuestsRotated: {e}"); }

            RaiseChanged();
        }

        /// <summary>
        /// Сверка сохранённого набора с текущим конфигом (в пределах периода):
        /// убранные из JSON квесты выбывают, новым целям ставится точка отсчёта,
        /// а если DailyCount вырос или квесты выбыли — набор добирается.
        /// </summary>
        private bool ReconcileActive()
        {
            bool dirty = false;
            var kept = new List<ActiveQuestState>();

            foreach (var entry in _state.Active)
            {
                if (entry == null || string.IsNullOrEmpty(entry.QuestId) ||
                    !_definitions.TryGetValue(new QuestId(entry.QuestId), out var def))
                {
                    dirty = true;
                    continue;
                }

                dirty |= EnsureBaselines(entry, def);
                kept.Add(entry);
            }

            // Набор на период намеренно не выдан — добирать нечего.
            int missing = _state.Withheld ? 0 : _settings.DailyCount - kept.Count;
            if (missing > 0)
            {
                var extra = DailyQuestSelector.Pick(
                    _pool,
                    missing,
                    Seed(_state.PeriodStartUtcTicks) + kept.Count,
                    exclude: ActiveIds(kept));

                foreach (var def in extra)
                    kept.Add(CreateEntry(def));

                dirty |= extra.Count > 0;
            }

            _state.Active = kept.ToArray();
            return dirty;
        }

        private DateTime LastResetUtc(DateTime now)
        {
            int hour = ((_settings.ResetHourUtc % 24) + 24) % 24;
            var reset = now.Date.AddHours(hour);
            if (now < reset)
                reset = reset.AddDays(-1);
            return reset;
        }

        private static int Seed(long periodStartTicks) =>
            (periodStartTicks / TimeSpan.TicksPerHour).GetHashCode();

        // --- Прогресс -------------------------------------------------------

        private ActiveQuestState CreateEntry(QuestDefinition def)
        {
            return new ActiveQuestState
            {
                QuestId = def.Id.Value,
                Baselines = def.Objectives
                    .Select(o => new ObjectiveBaseline { ObjectiveId = o.Id.Value, Value = ReadStat(o.StatPath) })
                    .ToArray()
            };
        }

        private bool EnsureBaselines(ActiveQuestState entry, QuestDefinition def)
        {
            var baselines = entry.Baselines?.Where(b => b != null).ToList() ?? new List<ObjectiveBaseline>();
            bool added = false;

            foreach (var obj in def.Objectives)
            {
                if (baselines.Any(b => b.ObjectiveId == obj.Id.Value)) continue;

                // Цель добавили в JSON посреди периода — считаем её с этой минуты.
                baselines.Add(new ObjectiveBaseline { ObjectiveId = obj.Id.Value, Value = ReadStat(obj.StatPath) });
                added = true;
            }

            entry.Baselines = baselines.ToArray();
            return added;
        }

        /// <summary>
        /// Защёлкнуть набранные цели, а за ними — квесты, у которых набраны все.
        /// true — что-то защёлкнулось, состояние надо сохранить.
        /// </summary>
        private bool EvaluateCompletion(bool notify)
        {
            bool changed = false;

            foreach (var entry in _state.Active)
            {
                if (entry.IsCompleted) continue;
                if (!_definitions.TryGetValue(new QuestId(entry.QuestId), out var def)) continue;

                bool allDone = true;
                foreach (var obj in def.Objectives)
                {
                    var baseline = BaselineOf(entry, obj);
                    if (baseline is { IsCompleted: true }) continue;

                    if (!ComputeProgress(entry, obj).IsComplete)
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

                if (!allDone) continue;

                entry.IsCompleted = true;
                changed = true;

                if (notify)
                    NotifyCompleted(entry);
            }

            return changed;
        }

        private QuestProgress ComputeProgress(ActiveQuestState entry, QuestObjective obj)
        {
            var baseline = BaselineOf(entry, obj);

            if (entry.IsCompleted || baseline is { IsCompleted: true })
                return new QuestProgress(obj.StatTarget, obj.StatTarget);

            long value = ReadStat(obj.StatPath);

            if (obj.Mode == QuestProgressMode.Delta)
                value -= baseline?.Value ?? value;

            // Обрезаем по цели: «57/50» в UI никому не нужно.
            value = Math.Max(0, Math.Min(value, obj.StatTarget));
            return new QuestProgress((int)value, obj.StatTarget);
        }

        private static ObjectiveBaseline BaselineOf(ActiveQuestState entry, QuestObjective obj) =>
            entry.Baselines?.FirstOrDefault(b => b != null && b.ObjectiveId == obj.Id.Value);

        private long ReadStat(string path) => _stats?.GetAggregate(PathOf(path)) ?? 0;

        private StatPath PathOf(string raw)
        {
            if (!_paths.TryGetValue(raw, out var path))
            {
                path = new StatPath(raw);
                _paths[raw] = path;
            }
            return path;
        }

        // --- Статы ----------------------------------------------------------

        private void Subscribe()
        {
            if (_stats == null) return;

            foreach (var path in _paths.Values.Distinct())
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

            // Период сменился прямо сейчас — Rotate уже всё пересчитал и сообщил.
            if (EnsureCurrentPeriod()) return;

            if (!ActiveUses(path)) return;

            if (EvaluateCompletion(notify: true))
                Save();

            RaiseChanged();
        }

        private bool ActiveUses(StatPath path)
        {
            foreach (var entry in _state.Active)
            {
                if (entry.IsCompleted) continue;
                if (!_definitions.TryGetValue(new QuestId(entry.QuestId), out var def)) continue;
                if (def.Objectives.Any(o => PathOf(o.StatPath) == path && BaselineOf(entry, o) is not { IsCompleted: true }))
                    return true;
            }
            return false;
        }

        private void WarnUndefinedStats()
        {
            if (_stats == null)
            {
                Debug.LogWarning("DailyQuestService: статов нет — квесты не будут набирать прогресс.");
                return;
            }

            // Опечатка в statPath не ломает сборку — квест просто никогда не
            // выполнится. Ловим это хотя бы предупреждением в лог.
            foreach (var def in _pool)
            {
                foreach (var obj in def.Objectives)
                {
                    var path = PathOf(obj.StatPath);
                    if (!path.IsPattern && !_stats.IsDefined(path))
                        Debug.LogWarning($"DailyQuestService: квест '{def.Id}' смотрит на стат '{path}', которого нет — он не выполнится.");
                }
            }
        }

        // --- Вспомогательное ------------------------------------------------

        private ActiveQuestState Find(QuestId questId)
        {
            if (questId.IsNone) return null;
            return _state.Active.FirstOrDefault(e => e.QuestId == questId.Value);
        }

        private QuestStatus[] BuildActive() =>
            _state.Active.Select(BuildStatus).Where(s => s != null).ToArray();

        private QuestStatus BuildStatus(ActiveQuestState entry)
        {
            if (!_definitions.TryGetValue(new QuestId(entry.QuestId), out var def))
                return null;

            return new QuestStatus
            {
                Definition = def,
                State = entry.IsClaimed ? QuestState.Claimed
                    : entry.IsCompleted ? QuestState.Completed
                    : QuestState.InProgress,
                ObjectiveProgresses = def.Objectives
                    .Select(o => new QuestObjectiveProgress { ObjectiveId = o.Id, Progress = ComputeProgress(entry, o) })
                    .ToArray()
            };
        }

        private static List<QuestId> ActiveIds(IEnumerable<ActiveQuestState> entries) =>
            entries
                .Where(e => e != null && !string.IsNullOrEmpty(e.QuestId))
                .Select(e => new QuestId(e.QuestId))
                .ToList();

        private void NotifyCompleted(ActiveQuestState entry)
        {
            try { _bridge?.OnQuestCompleted(BuildStatus(entry)); }
            catch (Exception e) { Debug.LogError($"DailyQuestService: ошибка в OnQuestCompleted для '{entry.QuestId}': {e}"); }
        }

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogError($"DailyQuestService: ошибка в подписчике Changed: {e}"); }
        }

        private static void LogValidation(DailyQuestValidationResult result)
        {
            foreach (var error in result.Errors)
                Debug.LogError($"DailyQuestService: {error}");
            foreach (var warning in result.Warnings)
                Debug.LogWarning($"DailyQuestService: {warning}");
        }

        /// <summary>
        /// Записать состояние. Записи идут строго по очереди — следующая ждёт
        /// предыдущую, иначе старое состояние могло бы лечь поверх нового.
        /// Пишется то, что в памяти на момент записи, а не на момент вызова.
        /// false — запись не удалась (ошибка уже в логе).
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
                Debug.LogError($"DailyQuestService: ошибка сохранения состояния: {e.Message}");
                return false;
            }
        }
    }
}
