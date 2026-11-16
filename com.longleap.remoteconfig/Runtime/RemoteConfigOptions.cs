using System;

namespace LongLeap.RemoteConfig
{
    /// <summary>Settings of <see cref="RemoteConfigService"/>. Read once, in the constructor.</summary>
    public sealed class RemoteConfigOptions
    {
        /// <summary>Default <see cref="CacheKey"/>.</summary>
        public const string DefaultCacheKey = "rc_cache";

        /// <summary>
        /// <see cref="LongLeap.Core.IPlayerPrefs"/> key of the cache. Use a different key per
        /// Firebase project or environment, so one never serves the other's values.
        /// </summary>
        public string CacheKey { get; set; } = DefaultCacheKey;

        /// <summary>
        /// <c>true</c> (default): a successful fetch is applied at once and
        /// <see cref="RemoteConfigService.Updated"/> is raised.
        /// <c>false</c>: fetched values are only cached; they apply on the next launch, or
        /// earlier when the game calls <see cref="RemoteConfigService.Activate"/> at a safe moment.
        /// </summary>
        public bool ActivateOnFetch { get; set; } = true;

        /// <summary>
        /// A fetch younger than this is not repeated; <see cref="RemoteConfigService.FetchAsync"/>
        /// returns <see cref="RemoteConfigFetchStatus.Throttled"/>. The time of the last successful
        /// fetch is cached, so the interval also spans launches. <see cref="TimeSpan.Zero"/>
        /// (default) disables throttling.
        /// </summary>
        public TimeSpan MinimumFetchInterval { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Log a warning, once per key, when a <c>Get*</c> getter asks for a key the server does not
        /// have. Only after values have come from the server (a successful fetch or a loaded cache),
        /// so offline sessions stay quiet. Default <c>true</c>.
        /// </summary>
        public bool WarnOnMissingKeys { get; set; } = true;

        /// <summary>Clock for the fetch interval. Replace it in tests. Default <see cref="DateTime.UtcNow"/>.</summary>
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
    }
}
