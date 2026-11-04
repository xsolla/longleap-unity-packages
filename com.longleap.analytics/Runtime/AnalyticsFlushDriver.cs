using System;
using UnityEngine;

namespace LongLeap.Analytics
{
    // Connects an IAnalyticsService to the Unity player loop:
    // - Update drives the periodic flush and the retry backoff;
    // - pause (mobile, desktop) or focus loss (WebGL) marks the app as in
    //   background, which flushes and stops counting engagement time;
    // - quitting flushes one last time.
    //
    //     AnalyticsFlushDriver.Attach(analytics);
    //
    // The driver does not own the service: dispose the service yourself. On
    // WebGL also attach WebGlPageLifecycle (com.longleap.platform.webgl) so
    // closing the tab flushes too.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class AnalyticsFlushDriver : MonoBehaviour
    {
        private IAnalyticsService _service;

        public IAnalyticsService Service => _service;

        // Creates a hidden GameObject that survives scene loads. Play mode only.
        public static AnalyticsFlushDriver Attach(IAnalyticsService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));
            if (!Application.isPlaying)
                throw new InvalidOperationException("AnalyticsFlushDriver works in play mode only.");

            var go = new GameObject("[AnalyticsFlushDriver]");
            DontDestroyOnLoad(go);
            var driver = go.AddComponent<AnalyticsFlushDriver>();
            driver._service = service;
            return driver;
        }

        private bool IsLive => _service != null && !_service.IsDisposed;

        private void Update()
        {
            if (IsLive)
                _service.Update(Time.unscaledDeltaTime);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL players get no pause callback: focus is the closest signal.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (IsLive)
                _service.SetForeground(hasFocus);
        }
#else
        private void OnApplicationPause(bool paused)
        {
            if (IsLive)
                _service.SetForeground(!paused);
        }
#endif

        private void OnApplicationQuit()
        {
            if (IsLive)
                _service.SetForeground(false);
        }
    }
}
