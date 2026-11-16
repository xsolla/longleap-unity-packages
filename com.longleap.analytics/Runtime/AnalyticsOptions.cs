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

        // Failed answers from the server (HTTP 408/429/5xx) an event may get
        // before it is dropped. Network errors and timeouts do not count: an
        // offline session is kept until it can be sent, within MaxQueueSize
        // and MaxEventAge.
        public int MaxAttempts { get; set; } = 5;

        // Events older than this are dropped before sending. GA4 discards
        // events backdated by more than 72 hours, so a larger value only
        // wastes requests.
        public TimeSpan MaxEventAge { get; set; } = Ga4Rules.MaxEventAge;

        // Retry backoff: FlushIntervalSeconds * 2^(failures - 1), capped here.
        public float MaxRetryDelaySeconds { get; set; } = 300f;

        // Inactivity (no events, app in background) after which a new session starts.
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

        // Initial consent state. When false, Send drops events until
        // SetCollectionEnabled(true) is called.
        public bool CollectionEnabled { get; set; } = true;

        // Optional event sent when the app goes to background, so the
        // engagement time since the last event is reported before a possible
        // kill or tab close. EventName.None (default) sends nothing. Use a
        // custom name such as "app_paused": GA4 reserves user_engagement.
        public EventName BackgroundEvent { get; set; } = EventName.None;

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
            if (MaxEventAge <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(MaxEventAge), MaxEventAge, "Must be positive.");
            if (SessionTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(SessionTimeout), SessionTimeout, "Must be positive.");
            if (Clock == null)
                throw new ArgumentNullException(nameof(Clock));
        }
    }
}
