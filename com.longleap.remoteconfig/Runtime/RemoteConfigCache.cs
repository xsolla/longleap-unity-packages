using System;
using System.Collections.Generic;
using LongLeap.Core;
using Newtonsoft.Json;

namespace LongLeap.RemoteConfig
{
    // The last successful fetch, stored as one JSON string in IPlayerPrefs:
    //
    //     {"v":1,"fetchedAtUnixMs":1762250000000,
    //      "parameters":{"key":"value"},
    //      "experiments":[{"id":"exp_01","variant":1}]}
    //
    // Experiments are cached with the parameters because a cached A/B variant without its
    // experiment tag would put untagged events into the experiment's analytics.
    internal static class RemoteConfigCache
    {
        public const int FormatVersion = 1;

        // The range DateTimeOffset.FromUnixTimeMilliseconds accepts (years 0001..9999).
        private static readonly long MinUnixMs = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
        private static readonly long MaxUnixMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

        public sealed class Entry
        {
            public IReadOnlyDictionary<string, string> Parameters;
            public IReadOnlyList<AbExperimentInfo> Experiments;
            public DateTime? FetchedAtUtc;
        }

        public enum LoadOutcome { Empty, Loaded, Discarded }

        public static LoadOutcome TryLoad(IPlayerPrefs prefs, string key, out Entry entry, out string problem)
        {
            entry = null;
            problem = null;

            var json = prefs.GetString(key, "");
            if (string.IsNullOrEmpty(json))
                return LoadOutcome.Empty;

            Data data;
            try
            {
                data = JsonConvert.DeserializeObject<Data>(json);
            }
            catch (JsonException ex)
            {
                problem = $"the cache is corrupted ({ex.Message})";
                return LoadOutcome.Discarded;
            }

            if (data == null || data.Version != FormatVersion || data.Parameters == null)
            {
                problem = $"the cache format is unknown (version {data?.Version.ToString() ?? "none"}, expected {FormatVersion})";
                return LoadOutcome.Discarded;
            }

            DateTime? fetchedAtUtc = null;
            if (data.FetchedAtUnixMs.HasValue)
            {
                var ms = data.FetchedAtUnixMs.Value;
                if (ms < MinUnixMs || ms > MaxUnixMs)
                {
                    problem = $"the cache has an invalid fetch time ({ms})";
                    return LoadOutcome.Discarded;
                }

                fetchedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }

            var experiments = new List<AbExperimentInfo>();
            if (data.Experiments != null)
            {
                foreach (var e in data.Experiments)
                {
                    if (e != null && !string.IsNullOrEmpty(e.Id))
                        experiments.Add(new AbExperimentInfo(e.Id, e.Variant));
                }
            }

            entry = new Entry
            {
                Parameters = data.Parameters,
                Experiments = experiments,
                FetchedAtUtc = fetchedAtUtc,
            };
            return LoadOutcome.Loaded;
        }

        public static void Save(IPlayerPrefs prefs, string key, Entry entry)
        {
            var data = new Data
            {
                Version = FormatVersion,
                FetchedAtUnixMs = entry.FetchedAtUtc.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(entry.FetchedAtUtc.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
                    : (long?)null,
                Parameters = new Dictionary<string, string>(),
                Experiments = new List<ExperimentData>(),
            };

            foreach (var pair in entry.Parameters)
                data.Parameters[pair.Key] = pair.Value;
            foreach (var e in entry.Experiments)
                data.Experiments.Add(new ExperimentData { Id = e.ExperimentId, Variant = e.VariantIndex });

            prefs.SetString(key, JsonConvert.SerializeObject(data));
            prefs.Save();
        }

        private sealed class Data
        {
            [JsonProperty("v")] public int Version;
            [JsonProperty("fetchedAtUnixMs")] public long? FetchedAtUnixMs;
            [JsonProperty("parameters")] public Dictionary<string, string> Parameters;
            [JsonProperty("experiments")] public List<ExperimentData> Experiments;
        }

        private sealed class ExperimentData
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("variant")] public int Variant;
        }
    }
}
