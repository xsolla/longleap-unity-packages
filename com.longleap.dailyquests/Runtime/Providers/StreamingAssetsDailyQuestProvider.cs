using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Квесты из файла рядом с билдом: по умолчанию
    /// <c>StreamingAssets/DailyQuests/daily_quests.json</c>.
    ///
    /// Файл читается один раз: первый <see cref="LoadAsync"/> запускает чтение,
    /// следующие отдают ту же задачу. Поэтому провайдер можно создать и дёрнуть
    /// заранее, в начале загрузки, — к моменту, когда понадобится сервис,
    /// данные будут уже в памяти (на WebGL это сетевой запрос).
    /// </summary>
    public sealed class StreamingAssetsDailyQuestProvider : IDailyQuestProvider
    {
        public const string DefaultFileName = "daily_quests.json";

        public static string DefaultPath =>
            Path.Combine(Application.streamingAssetsPath, "DailyQuests", DefaultFileName);

        private readonly string _uri;
        private Task<DailyQuestConfig> _load;

        public StreamingAssetsDailyQuestProvider() : this(DefaultPath) { }

        public StreamingAssetsDailyQuestProvider(string uri)
        {
            _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        }

        public Task<DailyQuestConfig> LoadAsync() => _load ??= LoadInternalAsync();

        private async Task<DailyQuestConfig> LoadInternalAsync()
        {
            var text = await LoadFileAsync(_uri);
            if (text == null)
            {
                Debug.LogWarning($"StreamingAssetsDailyQuestProvider: файл '{_uri}' не найден — квестов нет.");
                return new DailyQuestConfig();
            }

            return JsonDailyQuestProvider.ParseSafe(text, _uri);
        }

        private static async Task<string> LoadFileAsync(string uri)
        {
#if UNITY_EDITOR
            var path = UriToFilePath(uri);
            return File.Exists(path) ? await Task.FromResult(File.ReadAllText(path)) : null;
#else
            using var request = UnityWebRequest.Get(uri);
            var operation = request.SendWebRequest();
            var tcs = new TaskCompletionSource<string>();
            operation.completed += _ =>
                tcs.SetResult(request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null);
            return await tcs.Task;
#endif
        }

        private static string UriToFilePath(string uri)
        {
            if (uri.StartsWith("file://")) return uri.Substring(7);
            if (uri.StartsWith("file:")) return uri.Substring(5);
            return uri;
        }
    }
}
