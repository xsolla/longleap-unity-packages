using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.Accounts
{
    // Waiting with a timeout, frame by frame.
    //
    // Task.Delay and Task.WhenAny with a delay rely on System.Threading timers,
    // which never fire under WebGL. Task.Yield resumes on the next frame through
    // Unity's synchronization context, and a Stopwatch measures real time, so
    // this works the same in the editor, a player and the browser.
    internal static class FrameWait
    {
        // Wait until the task completes or the timeout runs out. true means the
        // task completed (successfully or not) within the timeout.
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

        // Wait until the condition becomes true. Same rules as ForAsync; a
        // negative timeout waits forever (the player's answer has no deadline).
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
