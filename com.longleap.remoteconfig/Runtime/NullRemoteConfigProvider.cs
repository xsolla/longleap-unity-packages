using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.RemoteConfig
{
    /// <summary>
    /// Remote config provider without a server, for offline builds and tests. Every fetch
    /// completes at once with <see cref="RemoteConfigFetchResult.Failure"/>, so
    /// <see cref="RemoteConfigService"/> keeps its cached values and getters fall back to
    /// their inline defaults.
    /// </summary>
    public sealed class NullRemoteConfigProvider : IRemoteConfigProvider
    {
        private static readonly Task<RemoteConfigFetchResult> Failed =
            Task.FromResult(RemoteConfigFetchResult.Failure);

        public Task<RemoteConfigFetchResult> FetchAsync(CancellationToken cancellationToken = default) => Failed;
    }
}
