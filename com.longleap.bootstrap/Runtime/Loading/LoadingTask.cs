using System;
using System.Threading.Tasks;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// Base for a loading task: only <see cref="ExecuteAsync"/> is left to write.
    /// The pipeline logs a failure with its exception; an ordinary task is then
    /// skipped and loading goes on, a critical one stops loading.
    /// </summary>
    public abstract class LoadingTask : ILoadingTask
    {
        /// <summary>Without this step the game cannot work: its failure stops loading.</summary>
        protected virtual bool IsCritical => false;

        public abstract Task ExecuteAsync(LoadingContext context);

        /// <summary>
        /// Override to roll back or substitute a stub; return the verdict
        /// (or <c>base.OnError</c>) as with <see cref="ILoadingTask.OnError"/>.
        /// </summary>
        public virtual bool OnError(Exception exception) => !IsCritical;
    }
}
