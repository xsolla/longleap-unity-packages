using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Quests from a TextAsset inside the build: by default
    /// <c>Resources/DailyQuests/daily_quests.json</c>.
    ///
    /// The file lives inside the build rather than next to it, as it did with StreamingAssets:
    /// not a single HTTP request. Portals (GamePix and others) repackage the build and
    /// serve it from their CDN, where the relative path StreamingAssets/... leads
    /// nowhere — the server answered with an HTML stub and the quest pool stayed empty.
    ///
    /// The file is read once: the first <see cref="LoadAsync"/> reads it,
    /// later calls return the same task. The read is synchronous, and
    /// <see cref="Resources.Load"/> may only be called from the main thread —
    /// so it is best to make the first call early, at the start of loading.
    /// </summary>
    public sealed class ResourcesDailyQuestProvider : IDailyQuestProvider
    {
        /// <summary>Path inside Resources, without extension.</summary>
        public const string DefaultPath = "DailyQuests/daily_quests";

        private readonly string _path;
        private Task<DailyQuestConfig> _load;

        public ResourcesDailyQuestProvider() : this(DefaultPath) { }

        /// <param name="path">Path inside Resources, without extension: "DailyQuests/daily_quests".</param>
        public ResourcesDailyQuestProvider(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public Task<DailyQuestConfig> LoadAsync() => _load ??= Task.FromResult(Load());

        private DailyQuestConfig Load()
        {
            var asset = Resources.Load<TextAsset>(_path);

            // No file — almost always a broken build, not "there are no quests".
            if (asset == null)
            {
                Debug.LogError($"ResourcesDailyQuestProvider: 'Resources/{_path}' not found — no quests.");
                return new DailyQuestConfig();
            }

            var text = asset.text;
            Resources.UnloadAsset(asset);

            return JsonDailyQuestProvider.ParseSafe(text, "Resources/" + _path);
        }
    }
}
