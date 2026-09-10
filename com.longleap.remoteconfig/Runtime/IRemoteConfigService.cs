using System.Threading.Tasks;
using LongLeap.Core;

namespace LongLeap.RemoteConfig
{
    public interface IRemoteConfigService : IExperimentInfoProvider
    {
        Task FetchAsync();
        string GetString(string key, string defaultValue);
        int GetInt(string key, int defaultValue);
        float GetFloat(string key, float defaultValue);
        bool GetBool(string key, bool defaultValue);
    }
}
