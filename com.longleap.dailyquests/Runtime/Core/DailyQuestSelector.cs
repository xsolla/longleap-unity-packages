using System;
using System.Collections.Generic;
using System.Linq;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Выбор набора квестов на период: взвешенно, без повторов внутри набора.
    ///
    /// Случайность сеется началом периода, поэтому набор детерминирован: один и
    /// тот же день у одного и того же пула даёт тот же набор (удобно в тестах и
    /// при разборе жалоб «у меня были другие квесты»).
    ///
    /// Квесты из <c>avoid</c> (вчерашний набор) берутся в последнюю очередь —
    /// только если без них набор не собрать.
    /// </summary>
    public static class DailyQuestSelector
    {
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

            var candidates = pool
                .Where(q => q != null && q.Weight > 0)
                .Where(q => exclude == null || !exclude.Contains(q.Id))
                .ToList();

            if (settings != null && (settings.UniqueCategories || settings.HardestLast))
            {
                var occupied = selected?.ToList() ?? new List<QuestDefinition>();
                int hardest = candidates.Concat(occupied).Select(q => q.Difficulty).DefaultIfEmpty(0).Max();
                // Choose the hard slot first, then backtrack if its categories block a full set.
                var slots = new List<int>(settings.SlotDifficulties ?? Array.Empty<int>());
                foreach (var quest in occupied) slots.Remove(quest.Difficulty);
                if (slots.Count > 0)
                    slots = slots.OrderByDescending(d => d).Take(count).ToList();
                else
                {
                    if (settings.HardestLast && !occupied.Any(q => q.Difficulty == hardest))
                        slots.Add(hardest);
                    while (slots.Count < count) slots.Add(-1);
                }
                Search(candidates, slots, 0, hardest, settings, avoid, random, occupied, result);
                return result.OrderBy(q => q.Difficulty).ToList();
            }

            var fresh = candidates.Where(q => avoid == null || !avoid.Contains(q.Id)).ToList();
            var stale = candidates.Where(q => avoid != null && avoid.Contains(q.Id)).ToList();

            TakeWeighted(fresh, count, random, result);
            TakeWeighted(stale, count, random, result);

            return result;
        }

        private static bool Search(List<QuestDefinition> pool, List<int> slots, int slot,
            int hardest, DailyQuestSettings settings, ICollection<QuestId> avoid,
            Random random, List<QuestDefinition> occupied, List<QuestDefinition> result)
        {
            if (slot == slots.Count) return true;
            var options = pool.Where(q => !occupied.Any(p => p.Id == q.Id))
                .Where(q => slots[slot] >= 0 ? q.Difficulty == slots[slot] :
                    !settings.HardestLast || q.Difficulty < hardest)
                .Where(q => !settings.UniqueCategories || !occupied.Any(p =>
                    (p.Categories ?? Array.Empty<string>()).Intersect(q.Categories ?? Array.Empty<string>()).Any()))
                .ToList();
            // Weighted permutation, with yesterday's quests tried after fresh candidates.
            var order = new List<QuestDefinition>();
            TakeWeighted(options.Where(q => avoid == null || !avoid.Contains(q.Id)).ToList(), options.Count, random, order);
            TakeWeighted(options.Where(q => avoid != null && avoid.Contains(q.Id)).ToList(), options.Count, random, order);
            foreach (var quest in order)
            {
                occupied.Add(quest);
                result.Add(quest);
                if (Search(pool, slots, slot + 1, hardest, settings, avoid, random, occupied, result)) return true;
                occupied.RemoveAt(occupied.Count - 1);
                result.RemoveAt(result.Count - 1);
            }
            return false;
        }

        private static void TakeWeighted(List<QuestDefinition> from, int count, Random random, List<QuestDefinition> into)
        {
            while (into.Count < count && from.Count > 0)
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

                into.Add(from[index]);
                from.RemoveAt(index);
            }
        }
    }
}
