using System.Threading.Tasks;
using LongLeap.RemoteConfig;

namespace LongLeap.Null
{
    public sealed class NullRemoteConfigBackend : IRemoteConfigProvider
    {
        public Task<RemoteConfigFetchResult> FetchAsync()
        {
            return Task.FromResult(new RemoteConfigFetchResult(null, null, false));
        }
    }
}
