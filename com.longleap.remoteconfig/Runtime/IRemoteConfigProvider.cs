using System.Threading.Tasks;

namespace LongLeap.RemoteConfig
{
    public interface IRemoteConfigProvider
    {
        Task<RemoteConfigFetchResult> FetchAsync();
    }
}
