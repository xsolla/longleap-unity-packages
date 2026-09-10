using System.Threading.Tasks;

namespace LongLeap.Core
{
    public interface IPersistentStorage
    {
        Task<string> LoadAsync(string key);
        Task SaveAsync(string key, string data);
        Task<bool> HasKeyAsync(string key);
        Task DeleteAsync(string key);
    }
}
