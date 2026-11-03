using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// Loads a scene asynchronously. Activation is deferred until loading is complete,
    /// so the player never sees a half-built world.
    /// Waiting is per-frame (Awaitable), with no threads — works in WebGL too.
    /// </summary>
    public sealed class LoadSceneTask : LoadingTask
    {
        /// <summary>AsyncOperation reports at most 0.9; the rest is spent on activation.</summary>
        private const float ReadyThreshold = 0.9f;

        private readonly string _sceneName;
        private readonly LoadSceneMode _mode;

        /// <param name="sceneName">Scene name or path, as for <see cref="SceneManager.LoadSceneAsync(string, LoadSceneMode)"/>.</param>
        public LoadSceneTask(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (string.IsNullOrEmpty(sceneName)) throw new ArgumentException("Empty scene name", nameof(sceneName));
            _sceneName = sceneName;
            _mode = mode;
        }

        /// <summary>Without the game scene there is nothing to play — stop loading.</summary>
        protected override bool IsCritical => true;

        public override async Task ExecuteAsync(LoadingContext context)
        {
            // The scene this operation produced. Looking it up by name afterwards could
            // return another loaded copy with the same name.
            Scene loaded = default;
            void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                if (!loaded.IsValid() && (scene.name == _sceneName || scene.path == _sceneName))
                    loaded = scene;
            }

            var operation = SceneManager.LoadSceneAsync(_sceneName, _mode);
            if (operation == null)
                throw new InvalidOperationException(
                    $"Scene '{_sceneName}' not found. Add it under File > Build Profiles > Scene List.");

            operation.allowSceneActivation = false;
            SceneManager.sceneLoaded += OnSceneLoaded;

            try
            {
                try
                {
                    while (operation.progress < ReadyThreshold)
                    {
                        context.ReportProgress(operation.progress / ReadyThreshold);
                        await Awaitable.NextFrameAsync(context.CancellationToken);
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
                await operation;
            }
            finally
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            // Give Awake/OnEnable of the new scene's objects a frame,
            // so the following tasks already see it ready.
            await Awaitable.NextFrameAsync();

            if (_mode == LoadSceneMode.Additive && loaded.IsValid() && loaded.isLoaded)
                SceneManager.SetActiveScene(loaded);
        }
    }
}
