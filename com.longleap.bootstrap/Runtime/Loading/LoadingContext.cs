using System;
using System.Threading;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// Everything a loading task gets from the outside: the service container, a
    /// cancellation token (the game was closed mid-load) and a way to report how far
    /// the step has got.
    /// </summary>
    public sealed class LoadingContext
    {
        private readonly Action<float> _progress;

        public LoadingContext(IServiceContainer services, CancellationToken cancellationToken, Action<float> progress = null)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            CancellationToken = cancellationToken;
            _progress = progress;
        }

        public IServiceContainer Services { get; }

        public CancellationToken CancellationToken { get; }

        public void ThrowIfCancelled() => CancellationToken.ThrowIfCancellationRequested();

        /// <summary>
        /// Progress of the current step, 0..1. Optional: a step that never reports
        /// jumps from 0 to 1 when it finishes.
        /// </summary>
        public void ReportProgress(float value) => _progress?.Invoke(value);
    }
}
