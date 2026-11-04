using System;

namespace LongLeap.Notifications
{
    /// <summary>
    /// A one-shot timer that fires a callback after a specified duration.
    /// </summary>
    public interface ITimer
    {
        /// <summary>
        /// Starts a countdown for the given <paramref name="duration"/>. If a countdown
        /// is already running, it is cancelled first. When the duration elapses,
        /// <paramref name="onElapsed"/> is invoked.
        /// </summary>
        void Start(TimeSpan duration, Action onElapsed);

        /// <summary>
        /// Cancels any running countdown without invoking the callback.
        /// </summary>
        void Cancel();
    }
}
