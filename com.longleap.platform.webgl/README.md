# LongLeap Platform WebGL

WebGL pieces for `com.longleap.analytics`:

- **`WebGlBeaconTransport`**: sends GA4 requests with `navigator.sendBeacon`. The browser delivers these even while the tab is closing; a `UnityWebRequest` still running at that moment is lost.
- **`WebGlPageLifecycle`**: reports page visibility to the analytics service. When the tab is hidden or closed, the queue is flushed while the page can still send. Unity raises no callback when a tab closes.

The JavaScript side (`Runtime/LongLeapAnalytics.jslib`) ships with the package. Nothing needs to be added to the WebGL template.

## Usage

```csharp
using LongLeap.Analytics;
using LongLeap.Platform.WebGL;

var settings = ga4ConfigAsset.ToSettings();
var backend = new Ga4Backend(settings, clientId, WebGlBeaconTransport.CreateForCurrentPlatform(settings));
var analytics = new AnalyticsService(backend);

AnalyticsFlushDriver.Attach(analytics);
WebGlPageLifecycle.Attach(analytics);
```

Both types compile on every platform. `CreateForCurrentPlatform(settings)` returns the beacon transport in WebGL players. It returns `UnityWebRequestAnalyticsTransport` everywhere else (including the Editor), and also whenever `settings.ValidateOnly` is set, because validation needs to read GA4's response. `WebGlPageLifecycle` does nothing outside WebGL players.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `WebGlBeaconTransport` | Sealed class, `IAnalyticsTransport`, `IAnalyticsTransportLimits` | Beacon POST, bodies ≤ 60 kB. `IsSupported`, `CreateForCurrentPlatform(settings)` |
| `WebGlPageLifecycle` | MonoBehaviour | `Attach(IAnalyticsService)` (one per service), `Detach()`. Hidden page → `SetForeground(false)`; visible again (including a back/forward cache restore) → `SetForeground(true)` |

## Behaviour worth knowing

- **Delivery is what the browser reports.** A beacon is fire-and-forget: the page never sees GA4's answer.
  - Accepted by the browser: counts as delivered (HTTP 204) and is never retried.
  - Refused: reported as a network error. The service logs it and retries with backoff, without using up an attempt.
  - There is no `fetch` fallback on purpose. `fetch` with `keepalive` shares the beacon quota and fails only later, after a success would already have been reported.
- **Quota.** Browsers refuse new beacons once those still in flight exceed about 64 KB. Each request is capped at 60 kB (`Ga4Backend` splits larger ones), so one request always fits on its own. A big flush at tab close can still be partly refused, and whatever is refused then is lost with the page. Frequent flushes (`BatchSize`, `FlushIntervalSeconds`) keep the final flush small.
- **CORS.** Bodies are sent as `text/plain` (a CORS "simple" request), so no preflight is needed. GA4 parses the JSON regardless of the content type.
- **Focus vs visibility.** `WebGlPageLifecycle.Attach` switches off `AnalyticsFlushDriver`'s focus tracking for the service. Losing focus (clicking outside the canvas on a portal page) no longer counts as background; only hiding the page does.
- **Listeners.** The jslib listens to `visibilitychange`, `pagehide` and `pageshow`, and calls `OnPageHidden` / `OnPageVisible` on the lifecycle GameObject via `SendMessage`. Duplicate states are filtered. `Detach()` (or destroying the object) removes the listeners.

## JavaScript functions

| Function | C# caller | Notes |
|---|---|---|
| `LongLeapAnalyticsSendBeacon(url, body)` | `WebGlBeaconTransport` | Returns 1 only when the browser queued the request |
| `LongLeapAnalyticsWatchVisibility(gameObjectName)` | `WebGlPageLifecycle.Attach` | Registers the visibility listeners |
| `LongLeapAnalyticsUnwatchVisibility(gameObjectName)` | `WebGlPageLifecycle.OnDestroy` | Removes them |

## Manual test checklist (WebGL build)

None of this runs in the Editor. Check it in a real build before each release, with the browser's DevTools Network tab open and filtered to `collect`:

1. **Normal sending:** play for a minute. Beacon requests (type `ping`/`beacon`) to `/mp/collect` appear about every `FlushIntervalSeconds` while events are sent.
2. **Tab switch:** send a few events and switch tabs straight away. A beacon goes out on hide.
3. **Tab close:** send a few events and close the tab. With DevTools "Preserve log" on, or in `chrome://net-export`, the final beacon is visible.
4. **Portal focus:** in an iframe portal (e.g. GamePix), click outside the canvas while the game stays visible. No flush, and engagement keeps counting.
5. **Back/forward cache:** navigate away and back. The game resumes, and events after the return carry a sensible `engagement_time_msec`.
6. **Validation:** build with `ValidateOnly` on. The console shows `Ga4Backend: validation passed` (this uses UnityWebRequest).
7. **GA4 DebugView / Realtime:** events appear with the right device category, OS and language.
