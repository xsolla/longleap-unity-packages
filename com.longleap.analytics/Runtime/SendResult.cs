namespace LongLeap.Analytics
{
    // Outcome of IAnalyticsBackend.SendAsync.
    //
    // Complete: the backend is done with every event in the batch (delivered,
    // or dropped for good and logged).
    //
    // RetryLater: the first HandledCount events are done; the rest go back to
    // the front of the queue and are retried with backoff. Of those, the first
    // AttemptedCount reached the destination and failed there (HTTP 408, 429,
    // 5xx): each such failure uses up one of AnalyticsOptions.MaxAttempts.
    // Events that never got an answer (no network, timeout) or were never
    // tried are retried without being charged, so an offline session is kept
    // until it can be sent (bounded by the queue size and the maximum event age).
    //
    // default(SendResult) means RetryLater(0, null, 0): nothing done, nothing charged.
    public readonly struct SendResult
    {
        public static readonly SendResult Complete = new SendResult(true, 0, 0, null);

        private SendResult(bool isComplete, int handledCount, int attemptedCount, string error)
        {
            IsComplete = isComplete;
            HandledCount = handledCount;
            AttemptedCount = attemptedCount;
            Error = error;
        }

        public bool IsComplete { get; }

        // Meaningful only for RetryLater.
        public int HandledCount { get; }

        // Meaningful only for RetryLater.
        public int AttemptedCount { get; }

        public string Error { get; }

        public static SendResult RetryLater(int handledCount, string error, int attemptedCount = 0) =>
            new SendResult(false, handledCount < 0 ? 0 : handledCount, attemptedCount < 0 ? 0 : attemptedCount, error);

        public override string ToString() =>
            IsComplete ? "Complete" : $"RetryLater(handled={HandledCount}, attempted={AttemptedCount}, error={Error})";
    }
}
