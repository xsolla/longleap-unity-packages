using System.Collections.Generic;
using System.Threading.Tasks;

namespace LongLeap.Analytics
{
    // Delivers batches of events somewhere: GA4, the console, a test spy.
    //
    // Contract:
    // - Called on the main thread, with at most one call in flight per
    //   AnalyticsService. The batch is never empty.
    // - Report delivery problems through SendResult instead of throwing. An
    //   exception is treated like RetryLater for the whole batch.
    // - Events that can never be delivered (rejected by the server, malformed)
    //   are the backend's to log and count as handled; retrying them would only
    //   repeat the failure.
    public interface IAnalyticsBackend
    {
        Task<SendResult> SendAsync(IReadOnlyList<AnalyticsEnvelope> batch);
    }
}
