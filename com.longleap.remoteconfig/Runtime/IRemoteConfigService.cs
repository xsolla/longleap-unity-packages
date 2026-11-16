using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core;

namespace LongLeap.RemoteConfig
{
    /// <summary>
    /// Typed remote parameters with inline defaults, plus the player's A/B experiments
    /// (<see cref="IExperimentInfoProvider.ActiveExperiments"/>) for analytics tagging.
    /// </summary>
    /// <remarks>
    /// Not thread-safe: call it from Unity's main thread.
    /// <para>
    /// <c>TryGet*</c> never logs. <c>Get*</c> returns the inline default when the key is missing
    /// or its value cannot be parsed, and logs a warning once per key.
    /// </para>
    /// <para>
    /// Parsing is culture-invariant and ignores surrounding whitespace. Integers also accept a
    /// whole number written as a decimal (<c>"42.0"</c>). Floating-point values must be finite.
    /// Booleans accept <c>true/t/yes/y/on/1</c> and <c>false/f/no/n/off/0</c>, case-insensitive,
    /// as the Firebase SDK does. JSON values are read with Newtonsoft.Json.
    /// </para>
    /// </remarks>
    public interface IRemoteConfigService : IExperimentInfoProvider, IDisposable
    {
        /// <summary>
        /// Raised after the active values change: a fetch applied them, or
        /// <see cref="Activate"/> was called. Not raised when values are loaded from the cache in
        /// the constructor.
        /// </summary>
        event Action Updated;

        /// <summary>
        /// Fetches fresh values from the provider. Calls made while a fetch is in flight share it.
        /// On success the values are cached and, unless <see cref="RemoteConfigOptions.ActivateOnFetch"/>
        /// is off, applied.
        /// </summary>
        /// <param name="cancellationToken">
        /// Stops waiting: the call returns <see cref="RemoteConfigFetchStatus.Canceled"/>. The request
        /// itself is not aborted (other callers may share it), and its result is still applied.
        /// Use it for a startup timeout.
        /// </param>
        Task<RemoteConfigFetchStatus> FetchAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies values fetched while <see cref="RemoteConfigOptions.ActivateOnFetch"/> is off.
        /// Returns <c>false</c> if there was nothing to apply.
        /// </summary>
        bool Activate();

        /// <summary><c>true</c> if a fetch has values waiting for <see cref="Activate"/>.</summary>
        bool HasPendingValues { get; }

        /// <summary>
        /// Outcome of the last request that reached the provider in this session
        /// (<see cref="RemoteConfigFetchStatus.Success"/> or <see cref="RemoteConfigFetchStatus.Failure"/>);
        /// <c>null</c> if none has completed yet.
        /// </summary>
        RemoteConfigFetchStatus? LastFetchStatus { get; }

        /// <summary>UTC time of the last successful fetch, also restored from the cache; <c>null</c> if never.</summary>
        DateTime? LastSuccessfulFetchUtc { get; }

        /// <summary>The active raw values by key. A read-only snapshot, replaced on every activation.</summary>
        IReadOnlyDictionary<string, string> Values { get; }

        /// <summary><c>true</c> if the active values contain <paramref name="key"/>.</summary>
        bool HasKey(string key);

        bool TryGetString(string key, out string value);
        bool TryGetInt(string key, out int value);
        bool TryGetLong(string key, out long value);
        bool TryGetFloat(string key, out float value);
        bool TryGetDouble(string key, out double value);
        bool TryGetBool(string key, out bool value);
        bool TryGetJson<T>(string key, out T value);

        string GetString(string key, string defaultValue);
        int GetInt(string key, int defaultValue);
        long GetLong(string key, long defaultValue);
        float GetFloat(string key, float defaultValue);
        double GetDouble(string key, double defaultValue);
        bool GetBool(string key, bool defaultValue);
        T GetJson<T>(string key, T defaultValue);
    }
}
