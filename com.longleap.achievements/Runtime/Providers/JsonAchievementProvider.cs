using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace LongLeap.Achievements
{
    /// <summary>
    /// Parses achievement definitions from JSON: a <c>manifest.json</c> listing the
    /// files, plus the files themselves. Where the text comes from is not its
    /// concern: the source is passed in (Resources in the game, a dictionary in
    /// tests), so the format can be tested without assets.
    /// Completes synchronously.
    /// </summary>
    public sealed class JsonAchievementProvider : IAchievementProvider
    {
        public const string ManifestFileName = "manifest.json";

        // File name from the manifest -> its text, or null if the file is missing.
        private readonly Func<string, string> _loadText;

        /// <param name="loadText">Returns the text of a file by name, or <c>null</c> if it does not exist.</param>
        public JsonAchievementProvider(Func<string, string> loadText)
        {
            _loadText = loadText ?? throw new ArgumentNullException(nameof(loadText));
        }

        public Task<AchievementDefinition[]> LoadAsync() => Task.FromResult(Load());

        private AchievementDefinition[] Load()
        {
            var manifest = LoadManifest();
            if (manifest.Length == 0)
                return Array.Empty<AchievementDefinition>();

            var allDefinitions = new List<AchievementDefinition>();

            // Manifest order sets the order of the definitions, and that in turn
            // sets the order of the cards in the collection.
            foreach (var fileName in manifest)
            {
                var text = _loadText(fileName);
                if (text == null)
                {
                    Debug.LogError($"JsonAchievementProvider: File '{fileName}' listed in {ManifestFileName} not found.");
                    continue;
                }

                try
                {
                    var dto = JsonConvert.DeserializeObject<AchievementFileDto>(text);
                    if (dto?.achievements == null) continue;

                    foreach (var entry in dto.achievements)
                        allDefinitions.Add(MapToDefinition(entry));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"JsonAchievementProvider: Failed to parse file '{fileName}': {ex.Message}");
                }
            }

            return allDefinitions.ToArray();
        }

        // An empty result here almost always means a broken build, not "there are
        // no achievements": without definitions GetStatus returns null, and
        // everything gated by achievements silently shows as unlocked. Hence
        // LogError rather than silence.
        private string[] LoadManifest()
        {
            var text = _loadText(ManifestFileName);
            if (text == null)
            {
                Debug.LogError($"JsonAchievementProvider: {ManifestFileName} not found.");
                return Array.Empty<string>();
            }

            try
            {
                var manifestDto = JsonConvert.DeserializeObject<ManifestDto>(text);
                return manifestDto?.files ?? Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"JsonAchievementProvider: Failed to parse {ManifestFileName}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static AchievementDefinition MapToDefinition(AchievementEntryDto entry)
        {
            return new AchievementDefinition
            {
                Id = new AchievementId(entry.id),
                Group = new GroupId(entry.group),
                IsHidden = entry.isHidden,
                Prerequisites = entry.prerequisites?.Select(p => new AchievementId(p)).ToArray() ?? Array.Empty<AchievementId>(),
                Objectives = entry.objectives?.Select(o =>
                {
                    var obj = new Objective { Id = new ObjectiveId(o.id) };
                    if (!string.IsNullOrEmpty(o.statPath))
                    {
                        return new Objective
                        {
                            Id = new ObjectiveId(o.id),
                            StatPath = o.statPath,
                            StatTarget = o.statTarget,
                        };
                    }
                    if (o.required != null && o.required.Length > 0)
                    {
                        return new Objective
                        {
                            Id = new ObjectiveId(o.id),
                            Required = o.required.Select(r => new AchievementId(r)).ToArray(),
                        };
                    }
                    return obj;
                }).ToArray() ?? Array.Empty<Objective>()
            };
        }

#pragma warning disable CS0649
        private sealed class ManifestDto
        {
            public string[] files;
        }

        private sealed class AchievementFileDto
        {
            public AchievementEntryDto[] achievements;
        }

        private sealed class AchievementEntryDto
        {
            public string id;
            public string group;
            public bool isHidden;
            public string[] prerequisites;
            public ObjectiveEntryDto[] objectives;
        }

        private sealed class ObjectiveEntryDto
        {
            public string id;
            public string statPath;
            public int statTarget;
            public string[] required;
        }
#pragma warning restore CS0649
    }
}
