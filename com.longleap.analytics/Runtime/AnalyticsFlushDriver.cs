using System;
using System.Collections.Generic;
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
    // One driver per service: Attach returns the existing driver when called
    // again. The driver does not own the service: dispose the service
    // yourself, and Detach the driver if the service is replaced.
    //
    // On WebGL attach WebGlPageLifecycle too. Page visibility is a better
    // background signal than focus (focus is also lost when the player clicks
    // outside the canvas, e.g. on a portal page), so WebGlPageLifecycle
    // switches focus tracking off for its service while it is attached.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class AnalyticsFlushDriver : MonoBehaviour
    {
        private static readonly Dictionary<IAnalyticsService, AnalyticsFlushDriver> Drivers =
            new Dictionary<IAnalyticsService, AnalyticsFlushDriver>();

        private static readonly HashSet<IAnalyticsService> FocusTrackingOff = new HashSet<IAnalyticsService>();

        private IAnalyticsService _service;

        public IAnalyticsService Service => _service;

        // Creates a hidden GameObject that survives scene loads, or returns
        // the driver already attached to this service. Play mode only.
        public static AnalyticsFlushDriver Attach(IAnalyticsService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));
            if (!Application.isPlaying)
                throw new InvalidOperationException("AnalyticsFlushDriver works in play mode only.");

            if (Drivers.TryGetValue(service, out var existing) && existing != null)
                return existing;

            var go = new GameObject("[AnalyticsFlushDriver]");
            DontDestroyOnLoad(go);
            var driver = go.AddComponent<AnalyticsFlushDriver>();
            driver._service = service;
            Drivers[service] = driver;
            return driver;
        }

        public static bool TryGet(IAnalyticsService service, out AnalyticsFlushDriver driver)
        {
            driver = null;
            return service != null && Drivers.TryGetValue(service, out driver) && driver != null;
        }

        // Stops using window focus as the background signal for this service.
        // For components that report visibility themselves (WebGlPageLifecycle).
        // Works whether the driver is attached before or after. Undo it with
        // EnableFocusTracking when that component goes away.
        public static void DisableFocusTracking(IAnalyticsService service)
        {
            if (service != null)
                FocusTrackingOff.Add(service);
        }

        // Uses window focus as the background signal again (the default) and
        // forgets the service, so the set does not keep it alive.
        public static void EnableFocusTracking(IAnalyticsService service)
        {
            if (service != null)
                FocusTrackingOff.Remove(service);
        }

        // Destroys the driver's GameObject. The service is left untouched.
        public void Detach()
        {
            Destroy(gameObject);
        }

        // Static state survives play mode sessions when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Drivers.Clear();
            FocusTrackingOff.Clear();
        }

        private bool IsLive => _service != null && !_service.IsDisposed;

        private void Update()
        {
            if (IsLive)
                _service.Update(Time.unscaledDeltaTime);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL players get no pause callback: focus is the fallback signal.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (IsLive && !FocusTrackingOff.Contains(_service))
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

        private void OnDestroy()
        {
            if (_service != null && Drivers.TryGetValue(_service, out var registered) && registered == this)
                Drivers.Remove(_service);
        }
    }
}
