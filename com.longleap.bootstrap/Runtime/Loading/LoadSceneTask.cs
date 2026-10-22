using System;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// Loads a scene asynchronously. Activation is deferred until loading is complete,
    /// so the player never sees a half-built world.
    /// Waiting is per-frame (Task.Yield), with no threads — works in WebGL too.
    /// </summary>
    public sealed class LoadSceneTask : ILoadingTask
    {
        /// <summary>AsyncOperation reports at most 0.9; the rest is spent on activation.</summary>
        private const float ReadyThreshold = 0.9f;

        private readonly string _sceneName;
        private readonly LoadSceneMode _mode;

        public LoadSceneTask(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (string.IsNullOrEmpty(sceneName)) throw new ArgumentException("Empty scene name", nameof(sceneName));
            _sceneName = sceneName;
            _mode = mode;
        }

        public async Task ExecuteAsync(LoadingContext context)
        {
            var operation = SceneManager.LoadSceneAsync(_sceneName, _mode);
            if (operation == null)
                throw new InvalidOperationException(
                    $"Scene '{_sceneName}' not found. Add it under File > Build Profiles > Scene List.");

            operation.allowSceneActivation = false;

            try
            {
                while (operation.progress < ReadyThreshold)
                {
                    context.ThrowIfCancelled();
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                // An operation with activation disabled must not be abandoned unfinished —
                // it would block every subsequent async load.
                operation.allowSceneActivation = true;
                throw;
            }

            operation.allowSceneActivation = true;
            while (!operation.isDone)
                await Task.Yield();

            // Give Awake/OnEnable of the new scene's objects a frame,
            // so the following tasks already see it ready.
            await Task.Yield();

            if (_mode == LoadSceneMode.Additive)
            {
                var scene = SceneManager.GetSceneByName(_sceneName);
                if (scene.IsValid()) SceneManager.SetActiveScene(scene);
            }
        }

        /// <summary>Without the game scene there is nothing to play — stop loading.</summary>
        public bool OnError(Exception exception) => false;
    }
}
