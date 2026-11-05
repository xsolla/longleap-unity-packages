using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace LongLeap.UI
{
    /// <summary>
    /// The UI root, which is also the <see cref="IWindowService"/>.
    ///
    /// Windows are not stored in the root prefab. The root holds the <c>Windows</c> catalog — a list of
    /// <see cref="WindowEntry"/>: a reference to the window prefab in the project plus its lifetime rules.
    /// The service itself creates a window under its root when it is needed and destroys it when it is
    /// not (the references are Addressables, so an unloaded window frees the prefab's memory too,
    /// not just the instance):
    /// <list type="bullet">
    /// <item><b>Preload</b> — created at startup (<see cref="InitializeAsync"/>), lives until exit;</item>
    /// <item>no flags — created on first open, then lives until exit;</item>
    /// <item><b>Unload On Close</b> — created on open, destroyed on close.</item>
    /// </list>
    ///
    /// Catalog row order is draw order: a lower row is drawn on top of an upper one,
    /// regardless of the order in which windows were created.
    ///
    /// The root prefab is instantiated once by the <c>WindowServiceTask</c> task, lives until
    /// the game exits (DontDestroyOnLoad) and survives scene changes. Loading is frame-by-frame,
    /// without threads — WebGL-safe. Open and close are async also so that a window
    /// can play its animation and the caller can wait for it.
    ///
    /// The service does not manage object activity: showing and hiding itself is the window's job
    /// (<see cref="WindowBase"/> enables and disables its Root). The service decides which window
    /// is created and open, and waits for its animations. It closes a new window from code right away,
    /// so it doesn't matter in what state the window prefab was saved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WindowService : MonoBehaviour, IWindowService
    {
        /// <summary>
        /// Default Addressables address of the root prefab — the game's loading sequence loads it by this key
        /// (it can also use its own key).
        /// Matches the asset path: this is exactly the address Addressables assigns by default
        /// when the prefab's Addressable checkbox is ticked.
        /// </summary>
        public const string AddressableKey = "Assets/Content/Prefab/UI/WindowServiceCanvas.prefab";

        [Tooltip("Window catalog: prefab + rules (preload, unload on close). " +
                 "Row order is draw order: a lower window is drawn on top of the ones above it.")]
        [SerializeField] private WindowEntry[] _windows;

        /// <summary>Runtime state of a single catalog row.</summary>
        private sealed class Slot
        {
            public WindowEntry Entry;
            public Type Type;

            /// <summary>Position in the catalog — which is also the draw order.</summary>
            public int Order;

            /// <summary>The created instance; null until the window is created or after it is unloaded.</summary>
            public WindowBase Instance;

            /// <summary>The prefab load that <see cref="Instance"/> was created from: released on unload.</summary>
            public AsyncOperationHandle<GameObject> Handle;

            /// <summary>Creation in progress right now: concurrent calls await the same task.</summary>
            public Task<WindowBase> Loading;

            public readonly List<Action<WindowBase>> Setups = new List<Action<WindowBase>>();
        }

        private readonly Dictionary<Type, Slot> _slots = new Dictionary<Type, Slot>();
        private readonly List<Slot> _ordered = new List<Slot>();
        private readonly HashSet<Type> _open = new HashSet<Type>();

        /// <summary>Where "back" on menu pages leads. Set by the game: <see cref="SetHome{TWindow}"/>.</summary>
        private Func<Task> _openHome;

        /// <summary>The EventSystem the UI works through: its own or one found in the scene.</summary>
        private EventSystem _eventSystem;

        /// <summary>
        /// Create windows flagged Preload. Called by WindowServiceTask after the root is
        /// instantiated. A failure of one window doesn't stop the others: it goes to the log,
        /// and opening that window later will try to create it again.
        /// </summary>
        public Task InitializeAsync()
        {
            var tasks = new List<Task>();

            foreach (var slot in _ordered)
            {
                if (slot.Entry.Preload)
                    tasks.Add(PreloadAsync(slot));
            }

            return Task.WhenAll(tasks);
        }

        public bool IsRegistered<TWindow>() where TWindow : class, IWindow => _slots.ContainsKey(typeof(TWindow));

        public bool TryGet<TWindow>(out TWindow window) where TWindow : class, IWindow
        {
            window = _slots.TryGetValue(typeof(TWindow), out var slot) && slot.Instance != null
                ? slot.Instance as TWindow
                : null;

            return window != null;
        }

        public async Task<TWindow> LoadAsync<TWindow>() where TWindow : class, IWindow =>
            await LoadSlotAsync(Resolve<TWindow>()) as TWindow;

        public async Task UnloadAsync<TWindow>() where TWindow : class, IWindow
        {
            var slot = Resolve<TWindow>();

            await CloseAsync(slot.Type);

            // The window may be in the middle of being created: wait for it, otherwise there would be
            // nothing to unload, and the instance created right after would be left hanging.
            if (slot.Loading != null)
            {
                try
                {
                    await slot.Loading;
                }
                catch
                {
                    // Not created — nothing to unload; whoever was creating it has already seen the reason.
                }
            }

            if (!_open.Contains(slot.Type))
                Unload(slot);
        }

        public void OnCreated<TWindow>(Action<TWindow> setup) where TWindow : class, IWindow
        {
            if (setup == null) return;

            if (!_slots.TryGetValue(typeof(TWindow), out var slot))
            {
                Debug.LogError(
                    $"[Windows] {typeof(TWindow).Name}: nowhere to attach the window setup — " +
                    $"it is not in the Windows catalog on prefab {AddressableKey}.");
                return;
            }

            void Apply(WindowBase window) => setup(window as TWindow);

            slot.Setups.Add(Apply);

            if (slot.Instance != null)
                RunSetup(slot, Apply);
        }

        public async Task<TWindow> OpenAsync<TWindow>() where TWindow : class, IWindow
        {
            var slot = Resolve<TWindow>();

            // Set the flag before await: two concurrent OpenAsync calls won't play the animation twice.
            var first = _open.Add(slot.Type);

            WindowBase window;
            try
            {
                window = await LoadSlotAsync(slot);
            }
            catch
            {
                // Not created — the window isn't open, and a repeated open should try again.
                if (first)
                    _open.Remove(slot.Type);

                throw;
            }

            // The window may have been closed while it was being created — nothing to show anymore.
            if (first && _open.Contains(slot.Type))
            {
                // Enabling its Root is the window's job: see WindowBase.OpenAsync.
                await window.OpenAsync();
                Debug.Log($"[Windows] opened {typeof(TWindow).Name}");
            }

            return window as TWindow;
        }

        public Task CloseAsync<TWindow>() where TWindow : class, IWindow => CloseAsync(typeof(TWindow));

        public Task CloseAsync(IWindow window) =>
            window != null ? CloseAsync(window.GetType()) : Task.CompletedTask;

        public async Task CloseAllAsync()
        {
            foreach (var type in new List<Type>(_open))
                await CloseAsync(type);
        }

        public bool IsOpen<TWindow>() where TWindow : class, IWindow => _open.Contains(typeof(TWindow));

        public void SetHome<TWindow>() where TWindow : class, IWindow
        {
            _openHome = async () => await OpenAsync<TWindow>();
        }

        public Task OpenHomeAsync()
        {
            if (_openHome != null) return _openHome();

            Debug.LogWarning("[Windows] home window is not set (SetHome) — 'back' has nowhere to go.");
            return Task.CompletedTask;
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            EnsureEventSystem();
            DisableForeignEventSystems();
            Index();

            // Visual press feedback is available even when audio has not loaded.
            if (GetComponent<MenuClickAudioBinder>() == null)
                gameObject.AddComponent<MenuClickAudioBinder>();

            // The game scene loads after the UI and may bring its own EventSystem.
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

#if UNITY_EDITOR
        /// <summary>Replace the whole catalog. Only needed by the tool that migrates windows out of the root.</summary>
        public void SetCatalog(WindowEntry[] entries)
        {
            _windows = entries;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>A row's type and label are taken from the prefab — they are not entered by hand.</summary>
        private void OnValidate()
        {
            if (_windows == null) return;

            foreach (var entry in _windows)
                entry?.Refresh();
        }
#endif

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => DisableForeignEventSystems();

        /// <summary>
        /// Indexes the catalog by window type. Windows themselves are not created here — it only
        /// checks that every row has a prefab and a known type.
        /// </summary>
        private void Index()
        {
            if (_windows == null) return;

            for (var i = 0; i < _windows.Length; i++)
            {
                var entry = _windows[i];

                if (entry == null || entry.Prefab == null || !entry.Prefab.RuntimeKeyIsValid())
                {
                    Debug.LogError($"[Windows] row {i} of the window catalog on prefab '{name}' has no prefab set.");
                    continue;
                }

                var type = string.IsNullOrEmpty(entry.TypeName) ? null : Type.GetType(entry.TypeName);
                if (type == null || !typeof(WindowBase).IsAssignableFrom(type))
                {
                    Debug.LogError(
                        $"[Windows] catalog row {i} ('{entry.Label}'): window type is not resolved. " +
                        $"Open prefab {AddressableKey} in the editor — the type will be picked up from the window prefab.");
                    continue;
                }

                if (_slots.ContainsKey(type))
                {
                    Debug.LogError(
                        $"[Windows] {type.Name} is in the catalog twice — the second row will not open.");
                    continue;
                }

                var slot = new Slot { Entry = entry, Type = type, Order = _ordered.Count };
                _slots.Add(type, slot);
                _ordered.Add(slot);
            }
        }

        private async Task CloseAsync(Type type)
        {
            if (!_slots.TryGetValue(type, out var slot)) return;

            // Remove from open before await: a repeated CloseAsync won't wait for the animation a second time.
            if (!_open.Remove(type)) return;

            // Closed while the window was still being created: showing never started, so there is
            // no exit animation — we wait for creation only to apply the unload rule.
            if (slot.Instance != null)
            {
                // Disabling its Root is the window's job: see WindowBase.CloseAsync.
                await slot.Instance.CloseAsync();
            }
            else if (slot.Loading != null)
            {
                try
                {
                    await slot.Loading;
                }
                catch
                {
                    // Not created — nothing to unload; whoever opened it will see the reason.
                    return;
                }
            }

            // The window may have been reopened while closing — it must not be unloaded.
            if (slot.Entry.UnloadOnClose && !_open.Contains(type))
                Unload(slot);
        }

        private Slot Resolve<TWindow>() where TWindow : class, IWindow
        {
            if (_slots.TryGetValue(typeof(TWindow), out var slot)) return slot;

            throw new InvalidOperationException(
                $"Window {typeof(TWindow).Name} is not registered: add its prefab to the Windows catalog " +
                $"on prefab {AddressableKey}.");
        }

        private async Task PreloadAsync(Slot slot)
        {
            try
            {
                await LoadSlotAsync(slot);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>The created window or the creation in progress; otherwise starts creating it.</summary>
        private Task<WindowBase> LoadSlotAsync(Slot slot)
        {
            if (slot.Instance != null)
                return Task.FromResult(slot.Instance);

            if (slot.Loading != null)
                return slot.Loading;

            var loading = CreateAsync(slot);

            // Completed synchronously — nothing to remember (and the finally in CreateAsync has already run).
            if (!loading.IsCompleted)
                slot.Loading = loading;

            return loading;
        }

        /// <summary>
        /// Loads the prefab and places an instance under the root. Everything after the load is in one block
        /// without await: the window is created, closed and set up in the same frame,
        /// so it never flashes on screen enabled.
        /// </summary>
        private async Task<WindowBase> CreateAsync(Slot slot)
        {
            var handle = Addressables.LoadAssetAsync<GameObject>(slot.Entry.Prefab.RuntimeKey);

            try
            {
                while (!handle.IsDone)
                    await Task.Yield();

                // The root was destroyed while loading (game exit): nowhere to create it.
                if (this == null)
                    throw new OperationCanceledException();

                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    var reason = handle.OperationException?.Message ?? "asset not found in Addressables";
                    throw new InvalidOperationException(
                        $"Window {slot.Type.Name} failed to load ({slot.Entry.Prefab.RuntimeKey}): {reason}");
                }

                var instance = Instantiate(handle.Result, transform, false);
                var window = instance.GetComponent<WindowBase>();

                if (window == null || !slot.Type.IsInstanceOfType(window))
                {
                    Destroy(instance);
                    throw new InvalidOperationException(
                        $"The root of window prefab '{handle.Result.name}' has no {slot.Type.Name} component: " +
                        "the catalog is outdated — open the service prefab in the editor to refresh it.");
                }

                // The window object is always enabled (see WindowBase): being saved disabled
                // doesn't work for it — the component lives on it.
                if (!instance.activeSelf)
                    instance.SetActive(true);

                instance.name = slot.Type.Name;

                // So the window can open the next one itself: see WindowBase.Windows.
                window.Bind(this);

                // The initial state of windows is set by code, not by how the prefab was saved.
                window.CloseImmediate();

                slot.Instance = window;
                slot.Handle = handle;

                PlaceInOrder(slot);

                foreach (var setup in slot.Setups.ToArray())
                    RunSetup(slot, setup);

                return window;
            }
            catch
            {
                // The handle went into the slot only together with the instance — otherwise it's ours to release.
                if (slot.Instance == null && handle.IsValid())
                    Addressables.Release(handle);

                throw;
            }
            finally
            {
                slot.Loading = null;
            }
        }

        /// <summary>
        /// Windows are created in arbitrary order but drawn in catalog order:
        /// place the new one right after the nearest already-created window above it in the list.
        /// </summary>
        private void PlaceInOrder(Slot slot)
        {
            var index = 0;

            for (var i = slot.Order - 1; i >= 0; i--)
            {
                var above = _ordered[i].Instance;
                if (above == null) continue;

                index = above.transform.GetSiblingIndex() + 1;
                break;
            }

            slot.Instance.transform.SetSiblingIndex(index);
        }

        private void RunSetup(Slot slot, Action<WindowBase> setup)
        {
            try
            {
                setup(slot.Instance);
            }
            catch (Exception exception)
            {
                // A broken setup must not leave the window uncreated.
                Debug.LogException(exception);
            }
        }

        /// <summary>Destroy the instance and release the prefab. The window will be recreated on the next open.</summary>
        private void Unload(Slot slot)
        {
            if (slot.Instance == null) return;

            var handle = slot.Handle;

            Destroy(slot.Instance.gameObject);
            slot.Instance = null;
            slot.Handle = default;

            Debug.Log($"[Windows] unloaded {slot.Type.Name}");

            _ = ReleaseAfterDestroyAsync(handle);
        }

        /// <summary>
        /// The prefab is released one frame later: Destroy takes effect at the end of the frame, and unloading
        /// the bundle right now would pull assets out from under the instance that is still alive this frame.
        /// </summary>
        private static async Task ReleaseAfterDestroyAsync(AsyncOperationHandle<GameObject> handle)
        {
            await Task.Yield();

            if (handle.IsValid())
                Addressables.Release(handle);
        }

        /// <summary>
        /// The UI comes up already in the startup scene, where there may be no EventSystem —
        /// without one, buttons can't be pressed. Our own EventSystem lives together with the UI root.
        /// </summary>
        private void EnsureEventSystem()
        {
            var host = new GameObject("[EventSystem]");
            host.transform.SetParent(transform, false);
            _eventSystem = host.AddComponent<EventSystem>();
            host.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>
        /// There must be exactly one EventSystem in the game. The UI lives with DontDestroyOnLoad and
        /// comes up before the game scene, so an EventSystem of the scene's own is a
        /// second one: two InputSystemUIInputModules share one set of input actions,
        /// the new one takes it over, and UI buttons stop responding. We disable the extra ones
        /// and report it in the log — such an object shouldn't be kept in the scene.
        /// </summary>
        private void DisableForeignEventSystems()
        {
            var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (systems.Length < 2) return;

            foreach (var system in systems)
            {
                if (system == null || system == _eventSystem) continue;

                Debug.LogWarning(
                    $"[Windows] found an extra EventSystem '{system.name}' in the scene — disabling it, " +
                    "otherwise UI buttons will stop responding. Remove this object from the scene.",
                    system);

                foreach (var module in system.GetComponents<BaseInputModule>())
                    module.enabled = false;

                system.enabled = false;
            }

            // Just in case: current may have pointed to the disabled one — restore ours.
            if (_eventSystem != null)
                EventSystem.current = _eventSystem;
        }
    }
}
