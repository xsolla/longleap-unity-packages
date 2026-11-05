using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.UI
{
    /// <summary>
    /// Images by URL: download once and hand out a ready sprite.
    ///
    /// Needed by <see cref="UserBackPackWindow"/>: backpack item icons come from the
    /// server as a link (iconUrl), not as a sprite in the project. The window is reopened on
    /// every run — without a cache the same images would be downloaded again each time.
    ///
    /// The cache holds a task, not a sprite: two cards with the same icon opened
    /// at the same time will wait for one download instead of starting two. A failed
    /// download is removed from the cache — we'll try again on the next open.
    ///
    /// Waiting is <c>while (!op.isDone) await Task.Yield()</c>, as in BackPackService:
    /// there are no threads under WebGL. There the image also needs CORS on the icon host side,
    /// otherwise the browser won't hand it over.
    /// </summary>
    public sealed class RemoteSpriteCache
    {
        private const int TimeoutSeconds = 15;

        private readonly Dictionary<string, Task<Sprite>> _loads = new Dictionary<string, Task<Sprite>>();

        /// <summary>Sprite by URL, or null if the download failed (reason is in the log).</summary>
        public async Task<Sprite> GetAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            if (!_loads.TryGetValue(url, out var load))
            {
                load = DownloadAsync(url);
                _loads[url] = load;
            }

            var sprite = await load;

            if (sprite == null && _loads.TryGetValue(url, out var current) && current == load)
                _loads.Remove(url);

            return sprite;
        }

        /// <summary>Release everything downloaded: textures won't leave memory on their own.</summary>
        public void Clear()
        {
            foreach (var load in _loads.Values)
            {
                if (!load.IsCompleted || load.Result == null) continue;

                var sprite = load.Result;
                Object.Destroy(sprite.texture);
                Object.Destroy(sprite);
            }

            _loads.Clear();
        }

        private static async Task<Sprite> DownloadAsync(string url)
        {
            using var request = UnityWebRequestTexture.GetTexture(url);
            request.timeout = TimeoutSeconds;

            var operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BackPack] icon failed to download: {url} — {request.error}");
                return null;
            }

            var texture = DownloadHandlerTexture.GetContent(request);
            if (texture == null) return null;

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            sprite.name = url;

            return sprite;
        }
    }
}
