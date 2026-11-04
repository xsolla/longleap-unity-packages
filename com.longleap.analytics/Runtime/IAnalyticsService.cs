using System;
using System.Threading.Tasks;

namespace LongLeap.Analytics
{
    // Entry point for game code. Main thread only: neither the queue nor the
    // session state is synchronized.
    public interface IAnalyticsService : IDisposable
    {
        // False while the player has not consented (or has withdrawn consent).
        bool IsCollectionEnabled { get; }

        // Current session ID (Unix seconds at session start).
        long SessionId { get; }

        // Events waiting for delivery, including ones waiting for a retry.
        int PendingCount { get; }

        bool IsDisposed { get; }

        // Consent switch. Disabling drops everything queued and ignores Send
        // until re-enabled. Requests already in flight still complete.
        void SetCollectionEnabled(bool enabled);

        // GA4 user_id, e.g. the account ID after sign-in. Null or empty clears
        // it. Applies to events sent from now on.
        void SetUserId(string userId);

        // GA4 user property. Null value removes it. Applies to events sent
        // from now on.
        void SetUserProperty(string name, string value);

        void AddEnricher(IAnalyticsEnricher enricher);

        void RemoveEnricher(IAnalyticsEnricher enricher);

        // Snapshots the event into the queue. Never throws for bad input;
        // problems are logged and the event or param is dropped.
        void Send(IAnalyticsEvent analyticsEvent);

        // Drives the periodic flush and the retry backoff. Call once per
        // frame with the unscaled delta time, or attach AnalyticsFlushDriver.
        void Update(float unscaledDeltaTime);

        // Tells the service the app went to background (false) or came back
        // (true). Going to background flushes immediately; background time
        // does not count as engagement; a long absence starts a new session.
        void SetForeground(bool isForeground);

        // Starts a flush now, ignoring the retry backoff. Fire-and-forget.
        void Flush();

        // Same as Flush; the task completes when the delivery attempt (and any
        // flush requested meanwhile) is over. Never faults.
        Task FlushAsync();
    }
}
