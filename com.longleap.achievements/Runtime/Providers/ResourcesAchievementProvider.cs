using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.Achievements
{
    /// <summary>
    /// Achievement definitions from <c>Resources/&lt;folder&gt;/</c>: <c>manifest.json</c>
    /// and the files it lists, parsed by <see cref="JsonAchievementProvider"/>.
    /// </summary>
    /// <remarks>
    /// The files ship inside the build rather than next to it (as they did with
    /// StreamingAssets), so loading makes no HTTP requests. Portals (GamePix and
    /// others) repackage the build and serve it from their own CDN, where a
    /// relative StreamingAssets/... path leads nowhere: the server answered with
    /// an HTML placeholder, the manifest failed to parse, and every
    /// achievement gate opened.
    ///
    /// <see cref="Resources.Load(string)"/> may only be called from the main thread.
    /// </remarks>
    public sealed class ResourcesAchievementProvider : IAchievementProvider
    {
        public const string DefaultFolder = "Achievements";

        private readonly JsonAchievementProvider _json;

        public ResourcesAchievementProvider() : this(DefaultFolder) { }

        /// <param name="folder">Folder relative to any <c>Resources</c> folder, e.g. <c>"Achievements"</c>.</param>
        public ResourcesAchievementProvider(string folder)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));

            folder = folder.TrimEnd('/');
            _json = new JsonAchievementProvider(fileName => LoadText(folder, fileName));
        }

        public Task<AchievementDefinition[]> LoadAsync() => _json.LoadAsync();

        private static string LoadText(string folder, string fileName)
        {
            // Resources.Load takes a path without the extension: "achievements.json" -> "achievements".
            var path = folder + "/" + Path.ChangeExtension(fileName, null);
            var asset = Resources.Load<TextAsset>(path);
            if (asset == null) return null;

            var text = asset.text;
            Resources.UnloadAsset(asset);
            return text;
        }
    }
}
