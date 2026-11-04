using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace LongLeap.Analytics
{
    // Forwards the browser's page visibility to an IAnalyticsService. A page
    // that becomes hidden (tab switched, minimized, closed) is reported as
    // background, which flushes the queue while the page can still send
    // beacons. Unity raises no callback when a tab closes, so without this the
    // last events of a session are lost.
    //
    //     AnalyticsFlushDriver.Attach(analytics);
    //     WebGlPageLifecycle.Attach(analytics);
    //
    // One instance per service: Attach returns the existing one when called
    // again. While attached, the flush driver's focus tracking is switched off
    // for the service, because visibility is the better signal on the web;
    // Detach (or destroying the object) switches it back on.
    //
    // The browser calls back directly (a function pointer plus an instance id),
    // not through SendMessage, so the GameObject's name does not matter.
    //
    // Does nothing outside WebGL players. The service is not owned here.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class WebGlPageLifecycle : MonoBehaviour
    {
        private static readonly Dictionary<IAnalyticsService, WebGlPageLifecycle> Instances =
            new Dictionary<IAnalyticsService, WebGlPageLifecycle>();

        private static readonly Dictionary<int, WebGlPageLifecycle> ById =
            new Dictionary<int, WebGlPageLifecycle>();

        private static int _nextId;

        private IAnalyticsService _service;
        private int _id;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Non-generic on purpose: IL2CPP cannot marshal generic delegates.
        private delegate void VisibilityCallback(int id, int hidden);

        // Kept in a field so the marshalled delegate is never collected.
        private static readonly VisibilityCallback Callback = OnVisibilityChanged;

        [DllImport("__Internal")]
        private static extern void LongLeapAnalyticsWatchVisibility(int id, VisibilityCallback callback);

        [DllImport("__Internal")]
        private static extern void LongLeapAnalyticsUnwatchVisibility(int id);

        // Called from LongLeapAnalytics.jslib. hidden: 1 hidden, 0 visible.
        [AOT.MonoPInvokeCallback(typeof(VisibilityCallback))]
        private static void OnVisibilityChanged(int id, int hidden)
        {
            // An exception must not escape into the browser's event handler.
            try
            {
                if (ById.TryGetValue(id, out var lifecycle) && lifecycle != null)
                    lifecycle.Report(hidden != 0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
#endif

        public IAnalyticsService Service => _service;

        // Play mode only.
        public static WebGlPageLifecycle Attach(IAnalyticsService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));
            if (!Application.isPlaying)
                throw new InvalidOperationException("WebGlPageLifecycle works in play mode only.");

            if (Instances.TryGetValue(service, out var existing) && existing != null)
                return existing;

            var go = new GameObject("[WebGlPageLifecycle]");
            DontDestroyOnLoad(go);
            var lifecycle = go.AddComponent<WebGlPageLifecycle>();
            lifecycle._service = service;
            lifecycle._id = ++_nextId;
            Instances[service] = lifecycle;
            ById[lifecycle._id] = lifecycle;

            AnalyticsFlushDriver.DisableFocusTracking(service);

#if UNITY_WEBGL && !UNITY_EDITOR
            LongLeapAnalyticsWatchVisibility(lifecycle._id, Callback);
#endif
            return lifecycle;
        }

        public static bool TryGet(IAnalyticsService service, out WebGlPageLifecycle lifecycle)
        {
            lifecycle = null;
            return service != null && Instances.TryGetValue(service, out lifecycle) && lifecycle != null;
        }

        // Removes the browser listeners, switches the flush driver's focus
        // tracking back on and destroys the GameObject.
        public void Detach()
        {
            Destroy(gameObject);
        }

        // Static state survives play mode sessions when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instances.Clear();
            ById.Clear();
            _nextId = 0;
        }

        private void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LongLeapAnalyticsUnwatchVisibility(_id);
#endif
            ById.Remove(_id);

            if (_service == null)
                return;

            if (Instances.TryGetValue(_service, out var registered) && registered == this)
            {
                Instances.Remove(_service);
                AnalyticsFlushDriver.EnableFocusTracking(_service);
            }
        }

        private void Report(bool hidden)
        {
            if (_service != null && !_service.IsDisposed)
                _service.SetForeground(!hidden);
        }
    }
}
