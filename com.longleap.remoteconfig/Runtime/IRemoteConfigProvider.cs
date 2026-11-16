using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.RemoteConfig
{
    /// <summary>
    /// Backend seam of <see cref="RemoteConfigService"/>: fetches the current parameters
    /// and A/B experiment assignments from a server.
    /// </summary>
    /// <remarks>
    /// Implementations: <c>FirebaseRemoteConfigProvider</c> (<c>com.longleap.firebase</c>) and
    /// <see cref="NullRemoteConfigProvider"/> (offline). The service does the caching,
    /// deduplication and activation, so a provider only performs one request per call.
    /// </remarks>
    public interface IRemoteConfigProvider
    {
        /// <summary>Fetches the parameters and experiments once.</summary>
        /// <param name="cancellationToken">
        /// Cancelled when the owning service is disposed. A provider should abort its request
        /// and may throw <see cref="System.OperationCanceledException"/>.
        /// </param>
        /// <returns>
        /// <see cref="RemoteConfigFetchResult.Success"/> with the full parameter set, or
        /// <see cref="RemoteConfigFetchResult.Failure"/>. A provider should report failures
        /// through the result rather than by throwing; the service treats an exception as a failure.
        /// </returns>
        Task<RemoteConfigFetchResult> FetchAsync(CancellationToken cancellationToken = default);
    }
}
