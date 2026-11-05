using System;
using System.IO;
using System.Threading.Tasks;

namespace LongLeap.Core
{
    /// <summary>
    /// <see cref="IPersistentStorage"/> backed by files: each key is stored as
    /// <c>&lt;directory&gt;/&lt;key&gt;.json</c>. The directory is created on the first save.
    /// Saves write a temp file first, then replace the old one.
    /// File access runs on the thread pool (<see cref="Task.Run(Action)"/>), so this
    /// class is not suitable for WebGL; use <see cref="PlayerPrefsPersistentStorage"/> there.
    /// </summary>
    public sealed class FilePersistentStorage : IPersistentStorage
    {
        private readonly string _directory;

        /// <param name="directory">Folder for the files, e.g. <c>Application.persistentDataPath + "/saves"</c>.</param>
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
