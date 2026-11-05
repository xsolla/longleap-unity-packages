using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// Base class for windows.
    ///
    /// A window is a separate prefab with this component on its root; the service creates it itself
    /// (see <see cref="WindowEntry"/>) and may destroy it after closing, so a window
    /// doesn't live forever: don't rely on anything inside it between showings.
    ///
    /// The window object itself is always enabled: the component lives on it, so the window can
    /// hold references, listen to events and compute things while closed. Only
    /// <see cref="Root"/> is enabled and disabled — the child object with all of the window's content:
    /// canvas, graphics, buttons.
    ///
    /// Window hierarchy in the UI prefab:
    /// <code>
    /// IntroWindow   (window component, always enabled)
    /// └── Root      (Canvas and all content, enabled on open)
    ///     ├── ...
    /// </code>
    ///
    /// Need an animation — override <see cref="OpenAsync"/>/<see cref="CloseAsync"/>:
    /// on open, call <c>base.OpenAsync()</c> first, then play the appear animation;
    /// on close, the other way round — the exit animation first, and only then <c>base.CloseAsync()</c>,
    /// otherwise there will be nothing left to disable. The service waits for both.
    /// </summary>
    public abstract class WindowBase : MonoBehaviour, IWindow
    {
        [Tooltip("Root of the window content: enabled on open, disabled on close.")]
        [SerializeField] private GameObject _root;

        /// <summary>
        /// The window content. Never null: if the reference wasn't assigned,
        /// the window controls its own object — it will work, but the component
        /// will be disabled along with the content.
        /// </summary>
        protected GameObject Root => _root != null ? _root : gameObject;

        /// <summary>The content is enabled right now.</summary>
        public bool IsOpen => Root.activeSelf;

        /// <summary>
        /// The service this window is registered in. Assigned when the window is created
        /// (after Awake, before the first show), so the window can open the next one itself
        /// without knowing who opened it:
        /// <c>Windows.OpenAsync&lt;NextWindow&gt;()</c>.
        /// </summary>
        protected IWindowService Windows { get; private set; }

        /// <summary>Called only by <see cref="WindowService"/> when the window is created.</summary>
        internal void Bind(IWindowService service) => Windows = service;

        /// <summary>Close itself — when the window sends the player onward and leaves after them.</summary>
        protected Task CloseSelfAsync() => Windows != null ? Windows.CloseAsync(this) : Task.CompletedTask;

        /// <summary>
        /// Start a transition from a button handler: <c>onClick</c> has no await,
        /// and an exception must not be silently swallowed — otherwise the window "won't open"
        /// without a single trace in the log.
        /// </summary>
        protected static async void Forget(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public virtual Task OpenAsync()
        {
            Root.SetActive(true);
            return Task.CompletedTask;
        }

        public virtual Task CloseAsync()
        {
            Root.SetActive(false);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Close without animation and without waiting. Needed by the service when creating a window: the initial
        /// state of windows is set by code, not by how the prefab was saved.
        /// </summary>
        public void CloseImmediate()
        {
            Root.SetActive(false);
        }

        protected virtual void Awake()
        {
            if (_root == null)
                Debug.LogError(
                    $"[Windows] window {GetType().Name} has no Root set — the window object itself will be disabled.",
                    this);
        }
    }
}
