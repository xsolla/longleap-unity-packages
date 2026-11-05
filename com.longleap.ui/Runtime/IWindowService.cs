using System;
using System.Threading.Tasks;

namespace LongLeap.UI
{
    /// <summary>
    /// Window service: opens and closes windows, and also decides when a window is created
    /// and when it is unloaded — this is set by <see cref="WindowEntry"/> in the service catalog.
    /// A window is addressed by its type: <c>OpenAsync&lt;SettingsWindow&gt;()</c>.
    /// Implementation — <see cref="WindowService"/>.
    ///
    /// Since a window may not exist yet (created on open) or may no longer
    /// exist (unloaded on close), the instance can't be treated as permanent:
    /// <list type="bullet">
    /// <item>to feed the window data — <see cref="OnCreated{TWindow}"/>: fires for
    /// every created instance, before it is shown;</item>
    /// <item>to get an instance without showing it — <see cref="LoadAsync{TWindow}"/>;</item>
    /// <item><see cref="TryGet{TWindow}"/> only covers windows that are already created.</item>
    /// </list>
    /// </summary>
    public interface IWindowService
    {
        /// <summary>The window is in the service catalog (whether it is currently created or not doesn't matter).</summary>
        bool IsRegistered<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// The window, if it is created right now. For windows that are unloaded or
        /// created on open, returns <c>false</c> between showings —
        /// for scene objects that need the window synchronously, it must be Preload.
        /// </summary>
        bool TryGet<TWindow>(out TWindow window) where TWindow : class, IWindow;

        /// <summary>
        /// Create the window (if not yet created) without opening it, and return the instance: to put
        /// data into it or subscribe to events before it is shown. Throws if the window
        /// is not in the catalog or the prefab failed to load.
        /// </summary>
        Task<TWindow> LoadAsync<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Unload the window now, without waiting for the rule: close it if open,
        /// destroy the instance and release the prefab.
        /// </summary>
        Task UnloadAsync<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Window setup: <paramref name="setup"/> is called immediately for an already created instance,
        /// and for ones created later — each time right after creation, before opening.
        /// This way data reaches the window whatever lifetime rule it has: an unloaded
        /// window is born "empty" again on its next open, and the setup runs again.
        /// If the window is not in the catalog — logs an error and does nothing.
        /// </summary>
        void OnCreated<TWindow>(Action<TWindow> setup) where TWindow : class, IWindow;

        /// <summary>Open the window (creating it if needed) and wait for its appear animation.</summary>
        Task<TWindow> OpenAsync<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Close the window and wait for its exit animation. Whether it is unloaded afterwards
        /// is decided by <see cref="WindowEntry.UnloadOnClose"/>.
        /// </summary>
        Task CloseAsync<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Same, by instance — when the type is only known at runtime:
        /// for example, a window closing itself (<see cref="WindowBase.CloseSelfAsync"/>).
        /// </summary>
        Task CloseAsync(IWindow window);

        Task CloseAllAsync();

        bool IsOpen<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Set the home window — where menu pages return on "back"
        /// (<see cref="MenuPageWindow"/>). Usually the main menu; set by the game during loading.
        /// </summary>
        void SetHome<TWindow>() where TWindow : class, IWindow;

        /// <summary>Open the home window. If not set — a warning is logged and nothing opens.</summary>
        Task OpenHomeAsync();
    }
}
