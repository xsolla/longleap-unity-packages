using System.Linq;

namespace LongLeap.DailyQuests
{
    /// <summary>Снимок одного активного квеста — то, что рисует UI.</summary>
    public sealed class QuestStatus
    {
        public QuestDefinition Definition { get; init; }
        public QuestState State { get; init; }
        public QuestObjectiveProgress[] ObjectiveProgresses { get; init; }

        public QuestId Id => Definition.Id;

        public bool CanClaim => State == QuestState.Completed;

        /// <summary>
        /// Прогресс квеста одной полоской — сумма по целям. Для квеста из одной
        /// цели это ровно её прогресс.
        /// </summary>
        public QuestProgress TotalProgress
        {
            get
            {
                if (ObjectiveProgresses == null || ObjectiveProgresses.Length == 0)
                    return new QuestProgress(0, 0);

                return new QuestProgress(
                    ObjectiveProgresses.Sum(o => o.Progress.Current),
                    ObjectiveProgresses.Sum(o => o.Progress.Target));
            }
        }
    }
}
