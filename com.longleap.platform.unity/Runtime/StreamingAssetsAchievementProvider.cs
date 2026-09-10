using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LongLeap.Achievements;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.Platform.Unity
{
    public sealed class StreamingAssetsAchievementProvider : IAchievementProvider
    {
        public static string DefaultBaseUri => Path.Combine(Application.streamingAssetsPath, "Achievements");

        private readonly string _baseUri;

        public StreamingAssetsAchievementProvider() : this(DefaultBaseUri + "/") { }

        public StreamingAssetsAchievementProvider(string baseUri)
        {
            _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
            if (!_baseUri.EndsWith("/"))
                _baseUri += "/";
        }

        public async Task<AchievementDefinition[]> LoadAsync()
        {
            var manifest = await LoadManifestAsync();
            if (manifest == null || manifest.Length == 0)
                return Array.Empty<AchievementDefinition>();

            // Файлы просим все разом, а не по одному. На WebGL каждый — отдельный
            // запрос к серверу, и выстроенные в очередь они складывались в
            // секунды холодной загрузки (пустой кеш браузера, приватная вкладка).
            // Параллельно это один поход туда-обратно вместо N.
            var loads = new Task<string>[manifest.Length];
            for (int i = 0; i < manifest.Length; i++)
                loads[i] = LoadFileAsync(_baseUri + manifest[i]);

            var texts = await Task.WhenAll(loads);

            var allDefinitions = new List<AchievementDefinition>();

            // Разбираем в порядке манифеста, а не в порядке ответов: иначе набор
            // определений (и порядок карточек в коллекции) зависел бы от того,
            // какой запрос вернулся первым, то есть менялся бы от запуска к запуску.
            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null) continue;

                try
                {
                    var dto = JsonConvert.DeserializeObject<AchievementFileDto>(text);
                    if (dto?.achievements == null) continue;

                    foreach (var entry in dto.achievements)
                        allDefinitions.Add(MapToDefinition(entry));
                }
                catch (Exception)
                {
                    Debug.LogWarning($"StreamingAssetsAchievementProvider: Failed to parse file '{manifest[i]}'.");
                }
            }

            return allDefinitions.ToArray();
        }

        private async Task<string[]> LoadManifestAsync()
        {
            var manifestUri = _baseUri + "manifest.json";
            var text = await LoadFileAsync(manifestUri);
            if (text == null) return Array.Empty<string>();

            try
            {
                var manifestDto = JsonConvert.DeserializeObject<ManifestDto>(text);
                return manifestDto?.files ?? Array.Empty<string>();
            }
            catch
            {
                Debug.LogWarning("StreamingAssetsAchievementProvider: Failed to parse manifest.json.");
                return Array.Empty<string>();
            }
        }

        private static async Task<string> LoadFileAsync(string uri)
        {
#if UNITY_EDITOR
            var path = UriToFilePath(uri);
            if (File.Exists(path))
                return await Task.FromResult(File.ReadAllText(path));
            return null;
#else
            using var request = UnityWebRequest.Get(uri);
            var operation = request.SendWebRequest();
            var tcs = new TaskCompletionSource<string>();
            operation.completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                    tcs.SetResult(request.downloadHandler.text);
                else
                    tcs.SetResult(null);
            };
            return await tcs.Task;
#endif
        }

        private static string UriToFilePath(string uri)
        {
            if (uri.StartsWith("file://")) return uri.Substring(7);
            if (uri.StartsWith("file:")) return uri.Substring(5);
            return uri;
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
