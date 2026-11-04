using System;

namespace LongLeap.Analytics
{
    // Settings for AnalyticsService. Read once, in the service constructor.
    public sealed class AnalyticsOptions
    {
        // A flush starts as soon as this many events are queued.
        public int BatchSize { get; set; } = 10;

        // AnalyticsService.Update flushes at least this often while events are queued.
        public float FlushIntervalSeconds { get; set; } = 5f;

        // Upper bound on queued events. When full, the oldest event is dropped.
        public int MaxQueueSize { get; set; } = 500;

        // Delivery attempts per event before it is dropped.
        public int MaxAttempts { get; set; } = 5;

        // Retry backoff: FlushIntervalSeconds * 2^(failures - 1), capped here.
        public float MaxRetryDelaySeconds { get; set; } = 300f;

        // Inactivity (no events, app in background) after which a new session starts.
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

        // Initial consent state. When false, Send drops events until
        // SetCollectionEnabled(true) is called.
        public bool CollectionEnabled { get; set; } = true;

        // Time source; replace in tests.
        public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

        internal void Validate()
        {
            if (BatchSize < 1)
                throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "Must be at least 1.");
            if (FlushIntervalSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(FlushIntervalSeconds), FlushIntervalSeconds, "Must be positive.");
            if (MaxQueueSize < BatchSize)
                throw new ArgumentOutOfRangeException(nameof(MaxQueueSize), MaxQueueSize, "Must be at least BatchSize.");
            if (MaxAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "Must be at least 1.");
            if (MaxRetryDelaySeconds < FlushIntervalSeconds)
                throw new ArgumentOutOfRangeException(nameof(MaxRetryDelaySeconds), MaxRetryDelaySeconds, "Must be at least FlushIntervalSeconds.");
            if (SessionTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(SessionTimeout), SessionTimeout, "Must be positive.");
            if (Clock == null)
                throw new ArgumentNullException(nameof(Clock));
        }
    }
}
