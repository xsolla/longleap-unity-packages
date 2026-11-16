using System;
using System.Threading.Tasks;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// A small loading task. One task — one self-contained step: bring up a service,
    /// wait for a config, warm a pool, load a scene.
    /// Tasks run sequentially in the order they were added to <see cref="LoadingPipeline"/>.
    /// </summary>
    public interface ILoadingTask
    {
        Task ExecuteAsync(LoadingContext context);

        /// <summary>
        /// The task failed. Roll back, substitute a stub, or just log here.
        /// Return true to skip the task and continue loading,
        /// false to stop loading.
        /// </summary>
        bool OnError(Exception exception);
    }
}
