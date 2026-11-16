using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core;
using UnityEngine;

namespace LongLeap.RemoteConfig
{
    /// <summary>
    /// Default <see cref="IRemoteConfigService"/>: reads values fetched by an
    /// <see cref="IRemoteConfigProvider"/>, caches the last successful fetch in
    /// <see cref="IPlayerPrefs"/> and serves typed getters with inline defaults.
    /// </summary>
    /// <remarks>
    /// The constructor loads the cache, so getters return the previous session's values before
    /// the first fetch completes. A cache in an unknown format is discarded with a warning.
    /// Not thread-safe: call it from Unity's main thread.
    /// </remarks>
    public sealed class RemoteConfigService : IRemoteConfigService
    {
        private static readonly IReadOnlyDictionary<string, string> NoValues =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        private readonly IRemoteConfigProvider _provider;
        private readonly IPlayerPrefs _playerPrefs;
        private readonly string _cacheKey;
        private readonly bool _activateOnFetch;
        private readonly TimeSpan _minimumFetchInterval;
        private readonly bool _warnOnMissingKeys;
        private readonly Func<DateTime> _utcNow;

        // Cancelled on Dispose; aborts the request in flight. Not disposed itself: without a
        // timer a CancellationTokenSource holds nothing, and Token would throw after Dispose.
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private readonly HashSet<string> _warnedKeys = new HashSet<string>(StringComparer.Ordinal);

        private IReadOnlyDictionary<string, string> _values = NoValues;
        private IReadOnlyList<AbExperimentInfo> _experiments = Array.Empty<AbExperimentInfo>();
        private RemoteConfigCache.Entry _pending;
        private bool _hasServerValues;
        private Task<RemoteConfigFetchStatus> _inFlight;
        private bool _disposed;

        /// <inheritdoc />
        public event Action Updated;

        /// <param name="provider">Where values are fetched from.</param>
        /// <param name="playerPrefs">Holds the cache between launches.</param>
        /// <param name="options">Settings; <c>null</c> means the defaults.</param>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="playerPrefs"/> is null.</exception>
        /// <exception cref="ArgumentException">An option is invalid.</exception>
        public RemoteConfigService(IRemoteConfigProvider provider, IPlayerPrefs playerPrefs, RemoteConfigOptions options = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _playerPrefs = playerPrefs ?? throw new ArgumentNullException(nameof(playerPrefs));

            options ??= new RemoteConfigOptions();
            if (string.IsNullOrEmpty(options.CacheKey))
                throw new ArgumentException("CacheKey cannot be empty.", nameof(options));
            if (options.MinimumFetchInterval < TimeSpan.Zero)
                throw new ArgumentException("MinimumFetchInterval cannot be negative.", nameof(options));

            _cacheKey = options.CacheKey;
            _activateOnFetch = options.ActivateOnFetch;
            _minimumFetchInterval = options.MinimumFetchInterval;
            _warnOnMissingKeys = options.WarnOnMissingKeys;
            _utcNow = options.UtcNow ?? throw new ArgumentException("UtcNow cannot be null.", nameof(options));

            LoadCache();
        }

        /// <inheritdoc />
        public IReadOnlyList<AbExperimentInfo> ActiveExperiments => _experiments;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> Values => _values;

        /// <inheritdoc />
        public bool HasPendingValues => _pending != null;

        /// <inheritdoc />
        public RemoteConfigFetchStatus? LastFetchStatus { get; private set; }

        /// <inheritdoc />
        public DateTime? LastSuccessfulFetchUtc { get; private set; }

        /// <inheritdoc />
        public Task<RemoteConfigFetchStatus> FetchAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
                return Task.FromResult(RemoteConfigFetchStatus.Canceled);

            var fetch = _inFlight;
            if (fetch == null)
            {
                if (IsThrottled())
                    return Task.FromResult(RemoteConfigFetchStatus.Throttled);

                fetch = RunFetchAsync();
                // A provider that completes synchronously finishes RunFetchAsync before this line;
                // a completed task must not be left behind as "in flight".
                _inFlight = fetch.IsCompleted ? null : fetch;
            }

            return cancellationToken.CanBeCanceled ? WaitAsync(fetch, cancellationToken) : fetch;
        }

        /// <inheritdoc />
        public bool Activate()
        {
            if (_disposed || _pending == null)
                return false;

            var entry = _pending;
            _pending = null;
            Apply(entry);
            return true;
        }

        /// <inheritdoc />
        public bool HasKey(string key) => _values.ContainsKey(CheckKey(key));

        /// <inheritdoc />
        public bool TryGetString(string key, out string value) => _values.TryGetValue(CheckKey(key), out value);

        /// <inheritdoc />
        public bool TryGetInt(string key, out int value)
        {
            value = 0;
            return TryGetString(key, out var raw) && RemoteConfigValueParser.TryParseInt(raw, out value);
        }

        /// <inheritdoc />
        public bool TryGetLong(string key, out long value)
        {
            value = 0;
            return TryGetString(key, out var raw) && RemoteConfigValueParser.TryParseLong(raw, out value);
        }

        /// <inheritdoc />
        public bool TryGetFloat(string key, out float value)
        {
            value = 0f;
            return TryGetString(key, out var raw) && RemoteConfigValueParser.TryParseFloat(raw, out value);
        }

        /// <inheritdoc />
        public bool TryGetDouble(string key, out double value)
        {
            value = 0d;
            return TryGetString(key, out var raw) && RemoteConfigValueParser.TryParseDouble(raw, out value);
        }

        /// <inheritdoc />
        public bool TryGetBool(string key, out bool value)
        {
            value = false;
            return TryGetString(key, out var raw) && RemoteConfigValueParser.TryParseBool(raw, out value);
        }

        /// <inheritdoc />
        public bool TryGetJson<T>(string key, out T value)
        {
            value = default;
            return TryGetString(key, out var raw) && RemoteConfigValueParser.TryParseJson(raw, out value);
        }

        /// <inheritdoc />
        public string GetString(string key, string defaultValue)
        {
            if (TryGetString(key, out var value))
                return value;
            WarnMissing(key);
            return defaultValue;
        }

        /// <inheritdoc />
        public int GetInt(string key, int defaultValue) =>
            Get<int>(key, defaultValue, RemoteConfigValueParser.TryParseInt, "int");

        /// <inheritdoc />
        public long GetLong(string key, long defaultValue) =>
            Get<long>(key, defaultValue, RemoteConfigValueParser.TryParseLong, "long");

        /// <inheritdoc />
        public float GetFloat(string key, float defaultValue) =>
            Get<float>(key, defaultValue, RemoteConfigValueParser.TryParseFloat, "float");

        /// <inheritdoc />
        public double GetDouble(string key, double defaultValue) =>
            Get<double>(key, defaultValue, RemoteConfigValueParser.TryParseDouble, "double");

        /// <inheritdoc />
        public bool GetBool(string key, bool defaultValue) =>
            Get<bool>(key, defaultValue, RemoteConfigValueParser.TryParseBool, "bool");

        /// <inheritdoc />
        public T GetJson<T>(string key, T defaultValue) =>
            Get<T>(key, defaultValue, RemoteConfigValueParser.TryParseJson, typeof(T).Name);

        /// <summary>
        /// Stops the service: a fetch in flight is aborted and its result dropped, getters return
        /// their defaults silently, and <see cref="Updated"/> handlers are removed.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lifetime.Cancel();
            _values = NoValues;
            _experiments = Array.Empty<AbExperimentInfo>();
            _pending = null;
            _inFlight = null;
            Updated = null;
        }

        private delegate bool Parser<T>(string raw, out T value);

        private T Get<T>(string key, T defaultValue, Parser<T> parse, string typeName)
        {
            if (!TryGetString(key, out var raw))
            {
                WarnMissing(key);
                return defaultValue;
            }

            if (parse(raw, out var value))
                return value;

            if (!_disposed && _warnedKeys.Add(key + "\n" + typeName))
                LogWarning($"'{key}'='{raw}' is not a valid {typeName}; using the default ({defaultValue?.ToString() ?? "null"}).");
            return defaultValue;
        }

        private async Task<RemoteConfigFetchStatus> RunFetchAsync()
        {
            try
            {
                RemoteConfigFetchResult result;
                try
                {
                    result = await _provider.FetchAsync(_lifetime.Token);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return RemoteConfigFetchStatus.Canceled;
                }
                catch (Exception ex)
                {
                    if (!_disposed)
                        LogWarning($"the provider threw: {ex.Message}");
                    result = null;
                }

                if (_disposed)
                    return RemoteConfigFetchStatus.Canceled;

                if (result == null || !result.IsSuccess)
                {
                    LastFetchStatus = RemoteConfigFetchStatus.Failure;
                    return RemoteConfigFetchStatus.Failure;
                }

                var entry = new RemoteConfigCache.Entry
                {
                    Parameters = new ReadOnlyDictionary<string, string>(Copy(result.Parameters)),
                    Experiments = new List<AbExperimentInfo>(result.Experiments).AsReadOnly(),
                    FetchedAtUtc = _utcNow(),
                };

                // The fetch itself succeeded: a failed write (e.g. the WebGL PlayerPrefs quota)
                // only costs the next launch its cache, so the values are still applied.
                try
                {
                    RemoteConfigCache.Save(_playerPrefs, _cacheKey, entry);
                }
                catch (Exception ex)
                {
                    LogWarning($"the cache could not be saved: {ex.Message}");
                }

                LastFetchStatus = RemoteConfigFetchStatus.Success;
                LastSuccessfulFetchUtc = entry.FetchedAtUtc;

                if (_activateOnFetch)
                {
                    _pending = null;
                    Apply(entry);
                }
                else
                {
                    _pending = entry;
                }

                return RemoteConfigFetchStatus.Success;
            }
            finally
            {
                _inFlight = null;
            }
        }

        private static async Task<RemoteConfigFetchStatus> WaitAsync(Task<RemoteConfigFetchStatus> fetch, CancellationToken cancellationToken)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => canceled.TrySetResult(true)))
            {
                var first = await Task.WhenAny(fetch, canceled.Task);
                return first == fetch ? await fetch : RemoteConfigFetchStatus.Canceled;
            }
        }

        private bool IsThrottled()
        {
            if (_minimumFetchInterval <= TimeSpan.Zero || !LastSuccessfulFetchUtc.HasValue)
                return false;

            var age = _utcNow() - LastSuccessfulFetchUtc.Value;
            // A negative age means the clock moved back; do not trust it.
            return age >= TimeSpan.Zero && age < _minimumFetchInterval;
        }

        private void Apply(RemoteConfigCache.Entry entry)
        {
            _values = entry.Parameters;
            _experiments = entry.Experiments;
            _hasServerValues = true;

            var handlers = Updated;
            if (handlers == null)
                return;

            // One failing handler must not stop the others or fail the fetch.
            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private void LoadCache()
        {
            RemoteConfigCache.LoadOutcome outcome;
            RemoteConfigCache.Entry entry;
            string problem;
            try
            {
                outcome = RemoteConfigCache.TryLoad(_playerPrefs, _cacheKey, out entry, out problem);
            }
            catch (Exception ex)
            {
                // A broken cache must never make the constructor throw on every launch.
                outcome = RemoteConfigCache.LoadOutcome.Discarded;
                entry = null;
                problem = $"the cache could not be read ({ex.Message})";
            }

            switch (outcome)
            {
                case RemoteConfigCache.LoadOutcome.Loaded:
                    _values = new ReadOnlyDictionary<string, string>(Copy(entry.Parameters));
                    _experiments = new List<AbExperimentInfo>(entry.Experiments).AsReadOnly();
                    LastSuccessfulFetchUtc = entry.FetchedAtUtc;
                    _hasServerValues = true;
                    break;
                case RemoteConfigCache.LoadOutcome.Discarded:
                    LogWarning($"{problem}; it is discarded and defaults are used until the next fetch.");
                    break;
            }
        }

        private void WarnMissing(string key)
        {
            if (_warnOnMissingKeys && _hasServerValues && !_disposed && _warnedKeys.Add(key))
                LogWarning($"key '{key}' is not in Remote Config; using the default.");
        }

        private static string CheckKey(string key) => key ?? throw new ArgumentNullException(nameof(key));

        private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> source)
        {
            var copy = new Dictionary<string, string>(source.Count, StringComparer.Ordinal);
            foreach (var pair in source)
                copy[pair.Key] = pair.Value;
            return copy;
        }

        private static void LogWarning(string message) =>
            Debug.LogWarning($"{nameof(RemoteConfigService)}: {message}");
    }
}
