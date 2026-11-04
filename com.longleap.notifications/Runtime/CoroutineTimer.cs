using System;
using System.Collections;
using UnityEngine;

namespace LongLeap.Notifications
{
    /// <summary>
    /// Default <see cref="ITimer"/>: a coroutine on a host MonoBehaviour waiting
    /// with <see cref="WaitForSecondsRealtime"/>, so it ignores <c>Time.timeScale</c>
    /// and keeps running while the game is paused.
    /// </summary>
    /// <remarks>
    /// Unity stops coroutines when the host GameObject is deactivated or destroyed;
    /// a running countdown then never fires. The callback runs no earlier than the
    /// next frame, even for a zero duration.
    /// </remarks>
    public sealed class CoroutineTimer : ITimer
    {
        private readonly MonoBehaviour _host;
        private Coroutine _coroutine;
        private Action _onElapsed;

        /// <param name="host">Runs the coroutine, usually the view that owns the timer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="host"/> is null or destroyed.</exception>
        public CoroutineTimer(MonoBehaviour host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _host = host;
        }

        /// <summary>
        /// Starts the countdown, cancelling a running one first. A negative
        /// <paramref name="duration"/> is treated as zero. Safe to call from
        /// inside <paramref name="onElapsed"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="onElapsed"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The host is destroyed or its GameObject is inactive.</exception>
        public void Start(TimeSpan duration, Action onElapsed)
        {
            if (onElapsed == null) throw new ArgumentNullException(nameof(onElapsed));

            Cancel();

            if (_host == null)
                throw new InvalidOperationException("CoroutineTimer: the host MonoBehaviour has been destroyed.");
            if (!_host.gameObject.activeInHierarchy)
                throw new InvalidOperationException(
                    $"CoroutineTimer: cannot start on '{_host.name}' because its GameObject is inactive.");

            if (duration < TimeSpan.Zero)
                duration = TimeSpan.Zero;

            _onElapsed = onElapsed;
            _coroutine = _host.StartCoroutine(CountdownRoutine(duration));
        }

        /// <summary>Stops the running countdown, if any, without invoking the callback.</summary>
        public void Cancel()
        {
            if (_coroutine != null)
            {
                // A destroyed host has already stopped its coroutines.
                if (_host != null)
                    _host.StopCoroutine(_coroutine);
                _coroutine = null;
            }
            _onElapsed = null;
        }

        private IEnumerator CountdownRoutine(TimeSpan duration)
        {
            yield return new WaitForSecondsRealtime((float)duration.TotalSeconds);
            // Clear state before invoking so the callback can restart the timer.
            var callback = _onElapsed;
            _onElapsed = null;
            _coroutine = null;
            callback?.Invoke();
        }
    }
}
