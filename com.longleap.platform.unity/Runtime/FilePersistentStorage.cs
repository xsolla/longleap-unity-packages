using System;
using System.IO;
using System.Threading.Tasks;
using LongLeap.Core;

namespace LongLeap.Platform.Unity
{
    public sealed class FilePersistentStorage : IPersistentStorage
    {
        private readonly string _directory;

        public FilePersistentStorage(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        private string GetPath(string key)
        {
            return Path.Combine(_directory, key + ".json");
        }

        public async Task<string> LoadAsync(string key)
        {
            return await Task.Run(() =>
            {
                var path = GetPath(key);
                if (!File.Exists(path))
                    return null;

                try
                {
                    return File.ReadAllText(path);
                }
                catch
                {
                    return null;
                }
            });
        }

        public async Task SaveAsync(string key, string data)
        {
            await Task.Run(() =>
            {
                var path = GetPath(key);
                var tempPath = path + ".tmp";

                try
                {
                    Directory.CreateDirectory(_directory);
                    File.WriteAllText(tempPath, data);
                    if (File.Exists(path))
                        File.Delete(path);
                    File.Move(tempPath, path);
                }
                catch
                {
                    UnityEngine.Debug.LogError($"[FilePersistentStorage] Failed to save '{key}' to {path}");
                }
            });
        }

        public async Task<bool> HasKeyAsync(string key)
        {
            return await Task.Run(() => File.Exists(GetPath(key)));
        }

        public async Task DeleteAsync(string key)
        {
            await Task.Run(() =>
            {
                var path = GetPath(key);
                if (File.Exists(path))
                    File.Delete(path);
            });
        }
    }
}
