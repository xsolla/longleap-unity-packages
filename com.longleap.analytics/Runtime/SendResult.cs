namespace LongLeap.Analytics
{
    // Outcome of IAnalyticsBackend.SendAsync.
    //
    // Complete: the backend is done with every event in the batch (delivered,
    // or dropped for good and logged).
    //
    // RetryLater: the first HandledCount events are done; the rest hit a
    // transient failure (no network, timeout, HTTP 5xx/429) and go back to the
    // front of the queue to be retried with backoff.
    public readonly struct SendResult
    {
        public static readonly SendResult Complete = new SendResult(true, 0, null);

        private SendResult(bool isComplete, int handledCount, string error)
        {
            IsComplete = isComplete;
            HandledCount = handledCount;
            Error = error;
        }

        public bool IsComplete { get; }

        // Meaningful only for RetryLater.
        public int HandledCount { get; }

        public string Error { get; }

        public static SendResult RetryLater(int handledCount, string error) =>
            new SendResult(false, handledCount < 0 ? 0 : handledCount, error);

        public override string ToString() =>
            IsComplete ? "Complete" : $"RetryLater(handled={HandledCount}, error={Error})";
    }
}
