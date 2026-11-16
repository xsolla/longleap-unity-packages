using System.Collections.Generic;
using System.Threading.Tasks;

namespace LongLeap.Analytics
{
    // Accepts and discards everything. Use it where analytics is switched off
    // for a whole build, so game code can call the service unconditionally.
    public sealed class NullAnalyticsBackend : IAnalyticsBackend
    {
        public static readonly NullAnalyticsBackend Instance = new NullAnalyticsBackend();

        private static readonly Task<SendResult> Completed = Task.FromResult(SendResult.Complete);

        public Task<SendResult> SendAsync(IReadOnlyList<AnalyticsEnvelope> batch) => Completed;
    }
}
