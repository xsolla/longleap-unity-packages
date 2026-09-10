using System.Threading.Tasks;
using LongLeap.Core;
using UnityEngine;

namespace LongLeap.Platform.Unity
{
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
