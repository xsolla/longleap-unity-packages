using System;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace LongLeap.Tools
{
    /// <summary>
    /// Whether the browser tab with the game is hidden: switched away from, minimized, or
    /// being closed. On the web this is the reliable "the player left" signal. Unity raises
    /// no callback when a tab closes, and window focus is also lost when the player clicks
    /// outside the canvas while the game stays on screen (a portal page around an iframe).
    ///
    /// <code>
    /// PageVisibility.Changed += hidden => { if (hidden) Save(); };
    /// </code>
    ///
    /// Listens to <c>visibilitychange</c>, <c>pagehide</c> and <c>pageshow</c> (a back/forward
    /// cache restore counts as shown again) through <c>LongLeapTools.jslib</c>. The browser
    /// listener is registered on the first subscription and kept for the page's lifetime.
    /// Duplicate states are filtered.
    ///
    /// Only WebGL players report anything (<see cref="IsSupported"/>). Elsewhere, including
    /// the Editor, <see cref="IsHidden"/> stays false and <see cref="Changed"/> never fires:
    /// keep <c>OnApplicationPause</c> for native mobile.
    ///
    /// A handler runs synchronously inside the browser event. Work it starts there (a beacon,
    /// a save) is the last chance before the page may be gone; an awaited continuation may
    /// never run.
    /// </summary>
    public static class PageVisibility
    {
        private static Action<bool> _changed;
        private static bool _watching;

        /// <summary>True in WebGL players, where the page reports visibility.</summary>
#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool IsSupported => true;
#else
        public static bool IsSupported => false;
#endif

        /// <summary>
        /// The tab is hidden right now. Up to date once anything has subscribed to
        /// <see cref="Changed"/>; always false where <see cref="IsSupported"/> is false.
        /// </summary>
        public static bool IsHidden { get; private set; }

        /// <summary>
        /// Raised with <c>true</c> when the tab becomes hidden and <c>false</c> when it is shown
        /// again. Main thread. An exception in one handler is logged and does not stop the
        /// others.
        /// </summary>
        public static event Action<bool> Changed
        {
            add
            {
                _changed += value;
                EnsureWatching();
            }
            remove => _changed -= value;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Non-generic on purpose: IL2CPP cannot marshal generic delegates.
        private delegate void VisibilityCallback(int hidden);

        // Kept in a field so the marshalled delegate is never collected.
        private static readonly VisibilityCallback Callback = OnBrowserVisibility;

        // Returns the current state: 1 hidden, 0 visible.
        [DllImport("__Internal")]
        private static extern int LongLeapToolsWatchVisibility(VisibilityCallback callback);

        [AOT.MonoPInvokeCallback(typeof(VisibilityCallback))]
        private static void OnBrowserVisibility(int hidden) => Report(hidden != 0);
#endif

        private static void EnsureWatching()
        {
            if (_watching)
                return;

            _watching = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            IsHidden = LongLeapToolsWatchVisibility(Callback) != 0;
#endif
        }

        private static void Report(bool hidden)
        {
            if (IsHidden == hidden)
                return;

            IsHidden = hidden;

            var handlers = _changed;
            if (handlers == null)
                return;

            // Each handler separately: an exception must neither skip the rest nor escape
            // into the browser's event handler.
            foreach (Action<bool> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(hidden);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        // Statics survive entering Play Mode without a domain reload. The browser listener
        // itself only exists in players, which have no such reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _changed = null;
            IsHidden = false;
#if UNITY_EDITOR
            _watching = false;
#endif
        }
    }
}
