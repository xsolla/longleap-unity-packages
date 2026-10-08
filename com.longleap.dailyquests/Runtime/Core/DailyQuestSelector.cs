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
            ICollection<QuestId> avoid = null)
        {
            var result = new List<QuestDefinition>();
            if (pool == null || count <= 0) return result;

            var random = new Random(seed);

            var candidates = pool
                .Where(q => q != null && q.Weight > 0)
                .Where(q => exclude == null || !exclude.Contains(q.Id))
                .ToList();

            var fresh = candidates.Where(q => avoid == null || !avoid.Contains(q.Id)).ToList();
            var stale = candidates.Where(q => avoid != null && avoid.Contains(q.Id)).ToList();

            TakeWeighted(fresh, count, random, result);
            TakeWeighted(stale, count, random, result);

            return result;
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
