using System;
using System.IO;
using System.Threading.Tasks;
using LongLeap.Achievements;
using UnityEngine;

namespace LongLeap.Platform.Unity
{
    // Определения ачивок из Resources/<folder>/ — manifest.json и файлы из него.
    //
    // Файлы лежат внутри билда, а не рядом с ним, как было со StreamingAssets:
    // ни одного HTTP-запроса. Порталы (GamePix и др.) перепаковывают билд и
    // отдают его со своего CDN, где относительный путь StreamingAssets/... ведёт
    // в никуда — сервер отвечал HTML-заглушкой, манифест не разбирался, и все
    // гейты по ачивкам открывались.
    //
    // Resources.Load можно звать только с главного потока.
    public sealed class ResourcesAchievementProvider : IAchievementProvider
    {
        public const string DefaultFolder = "Achievements";

        private readonly JsonAchievementProvider _json;

        public ResourcesAchievementProvider() : this(DefaultFolder) { }

        public ResourcesAchievementProvider(string folder)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));

            folder = folder.TrimEnd('/');
            _json = new JsonAchievementProvider(fileName => LoadText(folder, fileName));
        }

        public Task<AchievementDefinition[]> LoadAsync() => _json.LoadAsync();

        private static string LoadText(string folder, string fileName)
        {
            // Resources.Load принимает путь без расширения: «achievements.json» → «achievements».
            var path = folder + "/" + Path.ChangeExtension(fileName, null);
            var asset = Resources.Load<TextAsset>(path);
            if (asset == null) return null;

            var text = asset.text;
            Resources.UnloadAsset(asset);
            return text;
        }
    }
}
