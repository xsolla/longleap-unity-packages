using System;
using System.Collections.Generic;
using LongLeap.Analytics;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace LongLeap.Platform.WebGL
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
    // again. Attaching also switches off the flush driver's focus tracking for
    // the service, because visibility is the better signal on the web.
    //
    // Does nothing outside WebGL players. The service is not owned here.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class WebGlPageLifecycle : MonoBehaviour
    {
        private static readonly Dictionary<IAnalyticsService, WebGlPageLifecycle> Instances =
            new Dictionary<IAnalyticsService, WebGlPageLifecycle>();

        private static int _nextId;

        private IAnalyticsService _service;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void LongLeapAnalyticsWatchVisibility(string gameObjectName);

        [DllImport("__Internal")]
        private static extern void LongLeapAnalyticsUnwatchVisibility(string gameObjectName);
#endif

        // Play mode only.
        public static WebGlPageLifecycle Attach(IAnalyticsService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));
            if (!Application.isPlaying)
                throw new InvalidOperationException("WebGlPageLifecycle works in play mode only.");

            if (Instances.TryGetValue(service, out var existing) && existing != null)
                return existing;

            // The browser calls back through SendMessage, which finds the
            // object by name: keep names unique.
            var go = new GameObject("[WebGlPageLifecycle " + (++_nextId) + "]");
            DontDestroyOnLoad(go);
            var lifecycle = go.AddComponent<WebGlPageLifecycle>();
            lifecycle._service = service;
            Instances[service] = lifecycle;

            AnalyticsFlushDriver.DisableFocusTracking(service);

#if UNITY_WEBGL && !UNITY_EDITOR
            LongLeapAnalyticsWatchVisibility(go.name);
#endif
            return lifecycle;
        }

        // Removes the browser listeners and destroys the GameObject.
        public void Detach()
        {
            Destroy(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instances.Clear();
            _nextId = 0;
        }

        private void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LongLeapAnalyticsUnwatchVisibility(gameObject.name);
#endif
            if (_service != null && Instances.TryGetValue(_service, out var registered) && registered == this)
                Instances.Remove(_service);
        }

        // Called from LongLeapAnalytics.jslib.
        private void OnPageHidden()
        {
            if (_service != null && !_service.IsDisposed)
                _service.SetForeground(false);
        }

        // Called from LongLeapAnalytics.jslib.
        private void OnPageVisible()
        {
            if (_service != null && !_service.IsDisposed)
                _service.SetForeground(true);
        }
    }
}
