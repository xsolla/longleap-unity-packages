using System;
using System.Collections.Generic;
using System.Linq;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Picks the quest set for a period: weighted, no repeats within the set.
    ///
    /// The random generator is seeded with the period start, so the set is deterministic:
    /// the same day with the same pool always gives the same set (handy in tests and
    /// when looking into "I had different quests" reports).
    ///
    /// Quests from <c>avoid</c> (yesterday's set) are taken last —
    /// only when the set cannot be filled without them.
    ///
    /// Selection rules (<see cref="DailyQuestSettings.HasSelectionRules"/>):
    /// <list type="bullet">
    /// <item><c>SlotDifficulties</c> — the difficulty of each place in the set. Fewer slots
    /// than <c>DailyCount</c> — the rest take any difficulty; more — the hardest ones are used.</item>
    /// <item><c>HardestLast</c> — exactly one quest of the pool's highest difficulty
    /// (when no slots are set), the other places get easier quests.</item>
    /// <item><c>UniqueCategories</c> — no two quests in the set share a category.</item>
    /// </list>
    /// With rules the set is ordered by difficulty, easy to hard.
    ///
    /// If a full set cannot meet every rule, the rules are relaxed one by one:
    /// unique categories first (rewards depend on difficulty, so it matters more),
    /// then difficulty, and finally no rules at all. The player always gets
    /// as many quests as the pool allows.
    /// </summary>
    public static class DailyQuestSelector
    {
        /// <summary>
        /// How many options are tried at one relaxation level. Rules are checked
        /// by backtracking search, and on a large pool with unsatisfiable rules it
        /// would run for a very long time; once the budget is spent, the level fails.
        /// </summary>
        internal const int SearchBudget = 10_000;

        /// <param name="exclude">These quests are never picked.</param>
        /// <param name="avoid">These quests are taken only when the set cannot be filled without them.</param>
        /// <param name="settings">Selection rules; <c>null</c> — no rules.</param>
        /// <param name="selected">
        /// Quests already in the set (refill mid-period): they are not picked again
        /// and keep their slots and categories.
        /// </param>
        public static List<QuestDefinition> Pick(
            IReadOnlyList<QuestDefinition> pool,
            int count,
            int seed,
            ICollection<QuestId> exclude = null,
            ICollection<QuestId> avoid = null,
            DailyQuestSettings settings = null,
            IReadOnlyList<QuestDefinition> selected = null)
        {
            var result = new List<QuestDefinition>();
            if (pool == null || count <= 0) return result;

            var random = new Random(seed);
            var occupied = selected?.Where(q => q != null).ToList() ?? new List<QuestDefinition>();

            var candidates = pool
                .Where(q => q != null && q.Weight > 0)
                .Where(q => exclude == null || !exclude.Contains(q.Id))
                .Where(q => !occupied.Any(o => o.Id == q.Id))
                .ToList();

            if (settings == null || !settings.HasSelectionRules)
            {
                TakeFreshFirst(candidates, count, avoid, random, result);
                return result;
            }

            foreach (var level in Levels(settings))
            {
                if (TrySearch(candidates, count, settings, level, avoid, random, occupied, result))
                    return ByDifficulty(result);

                result.Clear();
            }

            // The rules cannot be met in any form — a full set without them beats a short one.
            TakeFreshFirst(candidates, count, avoid, random, result);
            return ByDifficulty(result);
        }

        /// <summary>
        /// Whether the pool can give a full set (<c>DailyCount</c>) under all rules,
        /// without relaxing them. For the validator.
        /// </summary>
        internal static bool CanSatisfy(IReadOnlyList<QuestDefinition> pool, DailyQuestSettings settings)
        {
            if (settings == null || !settings.HasSelectionRules || settings.DailyCount <= 0) return true;

            var candidates = (pool ?? Array.Empty<QuestDefinition>())
                .Where(q => q != null && q.Weight > 0)
                .ToList();

            return TrySearch(candidates, settings.DailyCount, settings, Levels(settings).First(),
                avoid: null, new Random(0), new List<QuestDefinition>(), new List<QuestDefinition>());
        }

        // --- Rules ----------------------------------------------------------

        /// <summary>Relaxation level: which rules still apply.</summary>
        private readonly struct Level
        {
            public readonly bool UniqueCategories;
            public readonly bool Difficulty;

            public Level(bool uniqueCategories, bool difficulty)
            {
                UniqueCategories = uniqueCategories;
                Difficulty = difficulty;
            }
        }

        private static IEnumerable<Level> Levels(DailyQuestSettings settings)
        {
            bool difficulty = settings.HardestLast || (settings.SlotDifficulties?.Length ?? 0) > 0;

            yield return new Level(settings.UniqueCategories, difficulty);

            if (settings.UniqueCategories && difficulty)
            {
                yield return new Level(uniqueCategories: false, difficulty: true);
                yield return new Level(uniqueCategories: true, difficulty: false);
            }
        }

        private static bool TrySearch(
            List<QuestDefinition> candidates, int count, DailyQuestSettings settings, Level level,
            ICollection<QuestId> avoid, Random random, List<QuestDefinition> occupied, List<QuestDefinition> result)
        {
            int hardest = candidates.Concat(occupied).Select(q => q.Difficulty).DefaultIfEmpty(0).Max();

            var search = new Search
            {
                Candidates = candidates,
                Slots = level.Difficulty
                    ? DifficultySlots(settings, occupied, hardest, count)
                    : Enumerable.Repeat<int?>(null, count).ToList(),
                Hardest = hardest,
                AnyBelowHardest = level.Difficulty && settings.HardestLast,
                UniqueCategories = level.UniqueCategories,
                Avoid = avoid,
                Random = random,
                Chosen = new List<QuestDefinition>(occupied),
                Result = result,
                Budget = SearchBudget
            };

            return search.Run(0);
        }

        /// <summary>
        /// Difficulty of each free place; <c>null</c> — any. The hardest go
        /// first: the pool has fewer such quests, so it pays to pick them early.
        /// </summary>
        private static List<int?> DifficultySlots(
            DailyQuestSettings settings, List<QuestDefinition> occupied, int hardest, int count)
        {
            var slots = new List<int?>();
            var wanted = (settings.SlotDifficulties ?? Array.Empty<int>()).ToList();

            if (wanted.Count > 0)
            {
                // Slots describe the whole set; quests already in it take their own.
                foreach (var quest in occupied)
                    wanted.Remove(quest.Difficulty);

                slots.AddRange(wanted.OrderByDescending(d => d).Select(d => (int?)d));
            }
            else if (settings.HardestLast && !occupied.Any(q => q.Difficulty == hardest))
            {
                slots.Add(hardest);
            }

            if (slots.Count > count)
                slots.RemoveRange(count, slots.Count - count);

            while (slots.Count < count)
                slots.Add(null);

            return slots;
        }

        /// <summary>Backtracking search: one slot per step, candidates in weighted order.</summary>
        private sealed class Search
        {
            public List<QuestDefinition> Candidates;
            public List<int?> Slots;
            public int Hardest;
            public bool AnyBelowHardest;
            public bool UniqueCategories;
            public ICollection<QuestId> Avoid;
            public Random Random;
            public List<QuestDefinition> Chosen;
            public List<QuestDefinition> Result;
            public int Budget;

            public bool Run(int slot)
            {
                if (slot == Slots.Count) return true;

                var fresh = new List<QuestDefinition>();
                var stale = new List<QuestDefinition>();

                foreach (var quest in Candidates)
                {
                    if (!Fits(quest, Slots[slot])) continue;
                    (Avoid != null && Avoid.Contains(quest.Id) ? stale : fresh).Add(quest);
                }

                // Weighted order, yesterday's quests after fresh ones.
                while (fresh.Count > 0 || stale.Count > 0)
                {
                    if (--Budget < 0) return false;

                    var quest = DrawWeighted(fresh.Count > 0 ? fresh : stale, Random);

                    Chosen.Add(quest);
                    Result.Add(quest);

                    if (Run(slot + 1)) return true;

                    Chosen.RemoveAt(Chosen.Count - 1);
                    Result.RemoveAt(Result.Count - 1);
                }

                return false;
            }

            private bool Fits(QuestDefinition quest, int? difficulty)
            {
                if (Chosen.Contains(quest)) return false;

                if (difficulty.HasValue
                        ? quest.Difficulty != difficulty.Value
                        : AnyBelowHardest && quest.Difficulty >= Hardest)
                    return false;

                if (UniqueCategories)
                {
                    foreach (var other in Chosen)
                        if (ShareCategory(quest, other)) return false;
                }

                return true;
            }
        }

        private static bool ShareCategory(QuestDefinition a, QuestDefinition b)
        {
            if (a.Categories == null || b.Categories == null) return false;

            foreach (var x in a.Categories)
            foreach (var y in b.Categories)
                if (string.Equals(x, y, StringComparison.Ordinal)) return true;

            return false;
        }

        // --- Weighted pick ----------------------------------------------------

        private static void TakeFreshFirst(
            List<QuestDefinition> candidates, int count, ICollection<QuestId> avoid, Random random, List<QuestDefinition> into)
        {
            var fresh = candidates.Where(q => avoid == null || !avoid.Contains(q.Id)).ToList();
            var stale = candidates.Where(q => avoid != null && avoid.Contains(q.Id)).ToList();

            TakeWeighted(fresh, count, random, into);
            TakeWeighted(stale, count, random, into);
        }

        private static void TakeWeighted(List<QuestDefinition> from, int count, Random random, List<QuestDefinition> into)
        {
            while (into.Count < count && from.Count > 0)
                into.Add(DrawWeighted(from, random));
        }

        /// <summary>Take one quest out of the list with probability proportional to its weight.</summary>
        private static QuestDefinition DrawWeighted(List<QuestDefinition> from, Random random)
        {
            long total = 0;
            foreach (var q in from)
                total += q.Weight;

            long roll = (long)(random.NextDouble() * total);
            int index = 0;
            for (long acc = 0; index < from.Count; index++)
            {
                acc += from[index].Weight;
                if (roll < acc) break;
            }

            if (index >= from.Count) index = from.Count - 1;

            var picked = from[index];
            from.RemoveAt(index);
            return picked;
        }

        private static List<QuestDefinition> ByDifficulty(List<QuestDefinition> quests) =>
            quests.OrderBy(q => q.Difficulty).ToList();
    }
}
