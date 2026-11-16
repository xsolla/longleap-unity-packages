namespace LongLeap.DailyQuests
{
    /// <summary>Snapshot of one active quest — what the UI draws.</summary>
    public sealed class QuestStatus
    {
        public QuestDefinition Definition { get; init; }
        public QuestState State { get; init; }
        public QuestObjectiveProgress[] ObjectiveProgresses { get; init; }

        public QuestId Id => Definition.Id;

        public bool CanClaim => State == QuestState.Completed;

        /// <summary>
        /// Quest progress as a single bar — the sum over objectives. For a
        /// single-objective quest this is exactly its progress.
        /// </summary>
        public QuestProgress TotalProgress
        {
            get
            {
                if (ObjectiveProgresses == null || ObjectiveProgresses.Length == 0)
                    return new QuestProgress(0, 0);

                // A loop rather than LINQ: the UI reads this on every redraw.
                int current = 0, target = 0;
                foreach (var objective in ObjectiveProgresses)
                {
                    current += objective.Progress.Current;
                    target += objective.Progress.Target;
                }

                return new QuestProgress(current, target);
            }
        }
    }
}
