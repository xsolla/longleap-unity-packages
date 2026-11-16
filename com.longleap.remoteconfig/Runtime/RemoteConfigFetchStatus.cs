namespace LongLeap.RemoteConfig
{
    /// <summary>Outcome of <see cref="IRemoteConfigService.FetchAsync"/>.</summary>
    public enum RemoteConfigFetchStatus
    {
        /// <summary>New values were fetched and cached (and activated, unless activation is deferred).</summary>
        Success,

        /// <summary>The provider failed or threw. The current values are kept.</summary>
        Failure,

        /// <summary>
        /// No request was made: the last successful fetch is younger than
        /// <see cref="RemoteConfigOptions.MinimumFetchInterval"/>.
        /// </summary>
        Throttled,

        /// <summary>
        /// The caller's token was cancelled, or the service was disposed. A request already in
        /// flight keeps running for other callers and its result is still applied, unless the
        /// service was disposed.
        /// </summary>
        Canceled,
    }
}
