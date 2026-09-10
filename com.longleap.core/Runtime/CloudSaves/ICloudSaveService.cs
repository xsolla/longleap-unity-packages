using System;
using System.Threading.Tasks;

namespace LongLeap.Core.CloudSaves
{
    public interface ICloudSaveService : IDisposable
    {
        Task InitializeAsync();
        Task SetStringAsync(CloudSaveKey key, string value);
        Task<string> GetStringAsync(CloudSaveKey key);
        Task<bool> HasKeyAsync(CloudSaveKey key);
        Task DeleteKeyAsync(CloudSaveKey key);
        Task DeleteAllAsync();
    }
}
