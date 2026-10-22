using System;
using System.Threading;
using LongLeap.Bootstrap;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// Everything a loading task gets from the outside: the service container and a
    /// cancellation token (the game was closed mid-load).
    /// </summary>
    public sealed class LoadingContext
    {
        public LoadingContext(IServiceContainer services, CancellationToken cancellationToken)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            CancellationToken = cancellationToken;
        }

        public IServiceContainer Services { get; }

        public CancellationToken CancellationToken { get; }

        public void ThrowIfCancelled() => CancellationToken.ThrowIfCancellationRequested();
    }
}
