using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Квесты из TextAsset внутри билда: по умолчанию
    /// <c>Resources/DailyQuests/daily_quests.json</c>.
    ///
    /// Файл лежит внутри билда, а не рядом с ним, как было со StreamingAssets:
    /// ни одного HTTP-запроса. Порталы (GamePix и др.) перепаковывают билд и
    /// отдают его со своего CDN, где относительный путь StreamingAssets/... ведёт
    /// в никуда — сервер отвечал HTML-заглушкой, и пул квестов оставался пустым.
    ///
    /// Файл читается один раз: первый <see cref="LoadAsync"/> читает его,
    /// следующие отдают ту же задачу. Чтение синхронное, и
    /// <see cref="Resources.Load"/> можно звать только с главного потока —
    /// поэтому первый вызов лучше сделать заранее, в начале загрузки.
    /// </summary>
    public sealed class ResourcesDailyQuestProvider : IDailyQuestProvider
    {
        /// <summary>Путь внутри Resources, без расширения.</summary>
        public const string DefaultPath = "DailyQuests/daily_quests";

        private readonly string _path;
        private Task<DailyQuestConfig> _load;

        public ResourcesDailyQuestProvider() : this(DefaultPath) { }

        /// <param name="path">Путь внутри Resources, без расширения: «DailyQuests/daily_quests».</param>
        public ResourcesDailyQuestProvider(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public Task<DailyQuestConfig> LoadAsync() => _load ??= Task.FromResult(Load());

        private DailyQuestConfig Load()
        {
            var asset = Resources.Load<TextAsset>(_path);

            // Файла нет — почти всегда поломка сборки, а не «квестов нет».
            if (asset == null)
            {
                Debug.LogError($"ResourcesDailyQuestProvider: 'Resources/{_path}' не найден — квестов нет.");
                return new DailyQuestConfig();
            }

            var text = asset.text;
            Resources.UnloadAsset(asset);

            return JsonDailyQuestProvider.ParseSafe(text, "Resources/" + _path);
        }
    }
}
