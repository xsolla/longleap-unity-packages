using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace LongLeap.UI
{
    /// <summary>
    /// A window catalog row in <see cref="WindowService"/>: the window prefab in the project
    /// and the rules by which the service manages it.
    ///
    /// Three modes come from two flags:
    /// <list type="bullet">
    /// <item><b>Preload</b> — the window is created right at UI startup and stays in memory.
    /// For windows that must open instantly, and those that code accesses
    /// synchronously via <see cref="IWindowService.TryGet{TWindow}"/>.</item>
    /// <item><b>no flags</b> — the window is created on first open and stays in memory:
    /// reopening is instant.</item>
    /// <item><b>Unload On Close</b> — the window is created on every open and unloaded
    /// on close: the instance is destroyed, the prefab is released from memory.
    /// For heavy and rarely used windows.</item>
    /// </list>
    /// Both flags together are also allowed: the window is created in advance for the first show,
    /// and after the first close it is unloaded and from then on follows the "create on open" rule.
    /// </summary>
    [Serializable]
    public sealed class WindowEntry
    {
        // First string field: Unity uses it as the list element label in the inspector.
        [Tooltip("Filled in automatically from the prefab — only the label in the list.")]
        [SerializeField] private string _label;

        [Tooltip("Window prefab in the project (Addressable). The window component is on the prefab root.")]
        [SerializeField] private AssetReferenceGameObject _prefab;

        [Tooltip("Window type. Filled in automatically from the prefab: a window is addressed by type, and the prefab " +
                 "can't be inspected before it is loaded — the type must be known in advance.")]
        [SerializeField] private string _typeName;

        [Tooltip("Create the window right at UI startup and keep it in memory.")]
        [SerializeField] private bool _preload;

        [Tooltip("Destroy the window and release the prefab on close; recreate it on open.")]
        [SerializeField] private bool _unloadOnClose;

        public AssetReferenceGameObject Prefab => _prefab;

        public string TypeName => _typeName;

        public bool Preload => _preload;

        public bool UnloadOnClose => _unloadOnClose;

        public string Label => string.IsNullOrEmpty(_label) ? _typeName : _label;

#if UNITY_EDITOR
        public WindowEntry()
        {
        }

        public WindowEntry(AssetReferenceGameObject prefab, bool preload, bool unloadOnClose)
        {
            _prefab = prefab;
            _preload = preload;
            _unloadOnClose = unloadOnClose;
            Refresh();
        }

        /// <summary>Pull the type and label from the prefab: called when the catalog is edited in the editor.</summary>
        public void Refresh()
        {
            var prefab = _prefab != null ? _prefab.editorAsset as GameObject : null;
            var window = prefab != null ? prefab.GetComponent<WindowBase>() : null;

            _typeName = window != null ? window.GetType().AssemblyQualifiedName : null;
            _label = window != null ? window.GetType().Name : prefab != null ? $"{prefab.name} (no WindowBase)" : "—";
        }
#endif
    }
}
