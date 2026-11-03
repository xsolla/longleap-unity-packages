using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// The queue of small loading tasks: runs them sequentially, logs the duration of
    /// each step and a summary at the end, reports progress and asks a failed task
    /// whether loading should continue.
    /// </summary>
    public sealed class LoadingPipeline
    {
        private readonly List<ILoadingTask> _tasks = new List<ILoadingTask>();
        private readonly List<float> _weights = new List<float>();

        public IReadOnlyList<ILoadingTask> Tasks => _tasks;

        public bool IsRunning { get; private set; }

        /// <summary>Last reported progress.</summary>
        public LoadingProgress Progress { get; private set; }

        /// <summary>Raised when a step starts, reports progress or finishes. Main thread.</summary>
        public event Action<LoadingProgress> ProgressChanged;

        /// <param name="weight">Share of the progress bar the step takes relative to the others.</param>
        public LoadingPipeline Add(ILoadingTask task, float weight = 1f)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (!(weight >= 0f) || float.IsInfinity(weight)) throw new ArgumentOutOfRangeException(nameof(weight), weight, "Weight must be a finite non-negative number.");
            if (IsRunning) throw new InvalidOperationException("The queue cannot be changed while loading is in progress.");

            _tasks.Add(task);
            _weights.Add(weight);
            return this;
        }

        /// <summary>Runs the queue. Returns false if an error or a cancellation stopped loading.</summary>
        public async Task<bool> RunAsync(IServiceContainer services, CancellationToken cancellationToken)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (IsRunning) throw new InvalidOperationException("Loading is already in progress.");

            IsRunning = true;

            float totalWeight = 0f;
            foreach (var weight in _weights) totalWeight += weight;

            int index = 0;
            float doneWeight = 0f;
            float stepProgress = 0f;
            var context = new LoadingContext(services, cancellationToken, value =>
            {
                value = Mathf.Clamp01(value);
                if (value <= stepProgress) return;
                stepProgress = value;
                Report(index, doneWeight, stepProgress, totalWeight);
            });

            var total = Stopwatch.StartNew();
            var skipped = new List<string>();

            try
            {
                for (; index < _tasks.Count; index++)
                {
                    var task = _tasks[index];
                    string step = $"{index + 1}/{_tasks.Count} {task.GetType().Name}";
                    var stopwatch = Stopwatch.StartNew();
                    stepProgress = 0f;
                    Report(index, doneWeight, 0f, totalWeight);

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await task.ExecuteAsync(context);
                        Debug.Log($"[Loading] {step} — ok, {stopwatch.ElapsedMilliseconds} ms");
                    }
                    // A cancellation of our own token means the game is closing. Any other
                    // OperationCanceledException (a request timeout inside the task) is
                    // an ordinary failure of the step.
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        Debug.LogWarning($"[Loading] {step} — cancelled after {stopwatch.ElapsedMilliseconds} ms");
                        LogSummary($"cancelled at {task.GetType().Name}", total, index, skipped, LogType.Warning);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        bool proceed;
                        try
                        {
                            proceed = task.OnError(ex);
                        }
                        catch (Exception handlerEx)
                        {
                            // Whether the handler rolled the step back is unknown — treat it as critical.
                            Debug.LogError($"[Loading] {step} — failed in {stopwatch.ElapsedMilliseconds} ms: {ex}");
                            Debug.LogError($"[Loading] {step} — OnError threw, loading stopped: {handlerEx}");
                            LogSummary($"stopped at {task.GetType().Name}", total, index, skipped, LogType.Error);
                            return false;
                        }

                        if (!proceed)
                        {
                            Debug.LogError($"[Loading] {step} — failed in {stopwatch.ElapsedMilliseconds} ms, loading stopped: {ex}");
                            LogSummary($"stopped at {task.GetType().Name}", total, index, skipped, LogType.Error);
                            return false;
                        }

                        Debug.LogError($"[Loading] {step} — failed in {stopwatch.ElapsedMilliseconds} ms, skipping: {ex}");
                        skipped.Add(task.GetType().Name);
                    }

                    doneWeight += _weights[index];
                    Report(index, doneWeight, 1f, totalWeight);
                }

                LogSummary("finished", total, _tasks.Count, skipped, skipped.Count > 0 ? LogType.Warning : LogType.Log);
                return true;
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void Report(int index, float doneWeight, float stepProgress, float totalWeight)
        {
            // Within a step doneWeight is what came before it; once the step is done it already includes it.
            float current = index < _weights.Count && stepProgress < 1f ? _weights[index] * stepProgress : 0f;
            float value = totalWeight > 0f ? Mathf.Clamp01((doneWeight + current) / totalWeight) : (stepProgress >= 1f ? 1f : 0f);
            string name = index < _tasks.Count ? _tasks[index].GetType().Name : null;
            Progress = new LoadingProgress(index, _tasks.Count, name, stepProgress, value);

            try
            {
                ProgressChanged?.Invoke(Progress);
            }
            catch (Exception ex)
            {
                // A broken progress bar must not break loading.
                Debug.LogError($"[Loading] a ProgressChanged subscriber threw: {ex}");
            }
        }

        private void LogSummary(string outcome, Stopwatch total, int completed, List<string> skipped, LogType type)
        {
            int ok = completed - skipped.Count;
            string message = $"[Loading] {outcome} in {total.ElapsedMilliseconds} ms " +
                             $"({Time.realtimeSinceStartup:0.00} s since start): {ok} ok, {skipped.Count} skipped";
            if (skipped.Count > 0) message += $" ({string.Join(", ", skipped)})";
            if (completed < _tasks.Count) message += $", {_tasks.Count - completed} not run";

            Debug.LogFormat(type, LogOption.None, null, "{0}", message);
        }
    }
}
