using System.Collections.Generic;
using System.Threading.Tasks;

namespace LongLeap.Core.CloudSaves
{
    public interface ICloudSaveProvider
    {
        Task SetStringAsync(CloudSaveKey key, string value);
        Task<string> GetStringAsync(CloudSaveKey key);
        Task<bool> HasKeyAsync(CloudSaveKey key);
        Task DeleteKeyAsync(CloudSaveKey key);
        Task<Dictionary<string, string>> LoadAllAsync();
        Task DeleteAllAsync();
    }
}
