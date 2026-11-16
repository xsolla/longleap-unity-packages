using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.XsollaId.Accounts
{
    /// <summary>
    /// Waiting with a timeout, frame by frame.
    /// </summary>
    /// <remarks>
    /// <c>Task.Delay</c> and <c>Task.WhenAny</c> with a delay rely on
    /// <c>System.Threading</c> timers, which never fire under WebGL.
    /// <c>Task.Yield</c> resumes on the next frame through Unity's
    /// synchronization context, and a <see cref="Stopwatch"/> measures real
    /// time, so this works the same in the editor, a player and the browser.
    /// </remarks>
    internal static class FrameWait
    {
        /// <summary>
        /// Wait until the task completes or the timeout runs out.
        /// </summary>
        /// <returns><c>true</c> if the task completed (successfully or not) within
        /// the timeout. A <c>null</c> task counts as completed.</returns>
        public static async Task<bool> ForAsync(Task task, double timeoutSec, CancellationToken token = default)
        {
            if (task == null)
                return true;

            var watch = Stopwatch.StartNew();

            while (!task.IsCompleted)
            {
                token.ThrowIfCancellationRequested();

                if (watch.Elapsed.TotalSeconds > timeoutSec)
                    return false;

                await Task.Yield();
            }

            return true;
        }

        /// <summary>
        /// Wait until the condition becomes true. Same rules as
        /// <see cref="ForAsync"/>; a negative timeout waits forever (the player's
        /// answer has no deadline).
        /// </summary>
        public static async Task<bool> UntilAsync(Func<bool> condition, double timeoutSec, CancellationToken token = default)
        {
            var watch = Stopwatch.StartNew();

            while (!condition())
            {
                token.ThrowIfCancellationRequested();

                if (timeoutSec >= 0 && watch.Elapsed.TotalSeconds > timeoutSec)
                    return false;

                await Task.Yield();
            }

            return true;
        }
    }
}
