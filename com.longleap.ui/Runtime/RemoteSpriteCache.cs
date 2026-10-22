using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.UI
{
    /// <summary>
    /// Картинки по адресу: скачать один раз и отдавать готовый спрайт.
    ///
    /// Нужен <see cref="UserBackPackWindow"/>: иконки предметов рюкзака приходят с
    /// сервера ссылкой (iconUrl), а не спрайтом в проекте. Окно переоткрывают на
    /// каждом заезде — без кэша каждый раз качали бы те же картинки заново.
    ///
    /// В кэше лежит задача, а не спрайт: две карточки с одной иконкой, открытые
    /// одновременно, дождутся одной загрузки, а не запустят две. Неудачная
    /// загрузка из кэша убирается — при следующем открытии попробуем снова.
    ///
    /// Ожидание — <c>while (!op.isDone) await Task.Yield()</c>, как в BackPackService:
    /// под WebGL потоков нет. Там же картинке нужен CORS на стороне хоста иконок,
    /// иначе браузер её не отдаст.
    /// </summary>
    public sealed class RemoteSpriteCache
    {
        private const int TimeoutSeconds = 15;

        private readonly Dictionary<string, Task<Sprite>> _loads = new Dictionary<string, Task<Sprite>>();

        /// <summary>Спрайт по адресу или null, если скачать не вышло (причина — в логе).</summary>
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

        /// <summary>Отпустить всё скачанное: текстуры сами из памяти не уйдут.</summary>
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
                Debug.LogWarning($"[BackPack] иконка не скачалась: {url} — {request.error}");
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
