using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.Core
{
    /// <summary>
    /// <see cref="IPersistentStorage"/> backed by Unity <see cref="PlayerPrefs"/>.
    /// Completes synchronously; call from the main thread. Every write and delete
    /// calls <see cref="PlayerPrefs.Save"/>, which on WebGL is a write to the
    /// browser's IndexedDB, so avoid it for data that changes every frame.
    /// Works on every platform, including WebGL.
    /// </summary>
    public sealed class PlayerPrefsPersistentStorage : IPersistentStorage
    {
        public Task<string> LoadAsync(string key)
        {
            if (!PlayerPrefs.HasKey(key))
                return Task.FromResult<string>(null);

            return Task.FromResult(PlayerPrefs.GetString(key));
        }

        public Task SaveAsync(string key, string data)
        {
            PlayerPrefs.SetString(key, data);
            PlayerPrefs.Save();
            return Task.CompletedTask;
        }

        public Task<bool> HasKeyAsync(string key)
        {
            return Task.FromResult(PlayerPrefs.HasKey(key));
        }

        public Task DeleteAsync(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return Task.CompletedTask;
        }
    }
}
