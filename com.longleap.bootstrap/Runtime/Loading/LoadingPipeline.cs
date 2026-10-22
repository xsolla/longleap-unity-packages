using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Bootstrap;
using Debug = UnityEngine.Debug;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// The queue of small loading tasks: runs them sequentially, logs the duration of
    /// each step and asks a failed task whether loading should continue.
    /// </summary>
    public sealed class LoadingPipeline
    {
        private readonly List<ILoadingTask> _tasks = new List<ILoadingTask>();

        public IReadOnlyList<ILoadingTask> Tasks => _tasks;

        public bool IsRunning { get; private set; }

        public LoadingPipeline Add(ILoadingTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (IsRunning) throw new InvalidOperationException("The queue cannot be changed while loading is in progress.");

            _tasks.Add(task);
            return this;
        }

        /// <summary>Runs the queue. Returns false if an error or a cancellation stopped loading.</summary>
        public async Task<bool> RunAsync(IServiceContainer services, CancellationToken cancellationToken)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (IsRunning) throw new InvalidOperationException("Loading is already in progress.");

            IsRunning = true;
            var context = new LoadingContext(services, cancellationToken);

            try
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    var task = _tasks[i];
                    string name = task.GetType().Name;
                    var stopwatch = Stopwatch.StartNew();

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await task.ExecuteAsync(context);
                        Debug.Log($"[Loading] {i + 1}/{_tasks.Count} {name} — ok, {stopwatch.ElapsedMilliseconds} ms");
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.LogWarning($"[Loading] cancelled at step {name}");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        if (!task.OnError(ex))
                        {
                            Debug.LogError($"[Loading] {name} — loading stopped: {ex}");
                            return false;
                        }

                        Debug.LogWarning($"[Loading] {name} — failed, skipping: {ex.Message}");
                    }
                }

                return true;
            }
            finally
            {
                IsRunning = false;
            }
        }
    }
}
