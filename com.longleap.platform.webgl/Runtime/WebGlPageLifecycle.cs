using System;
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
    // Does nothing outside WebGL players. The service is not owned here.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class WebGlPageLifecycle : MonoBehaviour
    {
        private IAnalyticsService _service;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void LongLeapAnalyticsWatchVisibility(string gameObjectName);
#endif

        // Play mode only. The GameObject name must be unique: the browser
        // calls back through SendMessage.
        public static WebGlPageLifecycle Attach(IAnalyticsService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));
            if (!Application.isPlaying)
                throw new InvalidOperationException("WebGlPageLifecycle works in play mode only.");

            var go = new GameObject("[WebGlPageLifecycle]");
            DontDestroyOnLoad(go);
            var lifecycle = go.AddComponent<WebGlPageLifecycle>();
            lifecycle._service = service;

#if UNITY_WEBGL && !UNITY_EDITOR
            LongLeapAnalyticsWatchVisibility(go.name);
#endif
            return lifecycle;
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
