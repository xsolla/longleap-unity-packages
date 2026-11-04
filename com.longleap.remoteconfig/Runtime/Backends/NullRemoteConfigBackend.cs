using System.Threading.Tasks;

namespace LongLeap.RemoteConfig
{
    /// <summary>
    /// Remote config backend without a server. Every fetch completes at once
    /// with <c>IsSuccess = false</c>, so <see cref="RemoteConfigService"/> keeps
    /// its cached values and getters fall back to their inline defaults.
    /// </summary>
    public sealed class NullRemoteConfigBackend : IRemoteConfigProvider
    {
        private static readonly Task<RemoteConfigFetchResult> Failed =
            Task.FromResult(new RemoteConfigFetchResult(null, null, false));

        public Task<RemoteConfigFetchResult> FetchAsync() => Failed;
    }
}
