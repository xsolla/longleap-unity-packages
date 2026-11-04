# LongLeap Platform WebGL

WebGL pieces for `com.longleap.analytics`:

- **`WebGlBeaconTransport`**: sends GA4 requests with `navigator.sendBeacon`, falling back to `fetch` with `keepalive`. The browser delivers these even while the tab is closing; a `UnityWebRequest` still running at that moment is lost.
- **`WebGlPageLifecycle`**: reports page visibility to the analytics service. When the tab is hidden or closed, the queue is flushed while the page can still send. Unity raises no callback when a tab closes.

The JavaScript side (`Runtime/LongLeapAnalytics.jslib`) ships with the package. Nothing needs to be added to the WebGL template.

## Usage

```csharp
using LongLeap.Analytics;
using LongLeap.Platform.WebGL;

var backend = new Ga4Backend(config, clientId, WebGlBeaconTransport.CreateForCurrentPlatform());
var analytics = new AnalyticsService(backend);

AnalyticsFlushDriver.Attach(analytics);
WebGlPageLifecycle.Attach(analytics);
```

Both types compile on every platform. `CreateForCurrentPlatform()` returns the beacon transport in WebGL players and `UnityWebRequestAnalyticsTransport` everywhere else, including the Editor. `WebGlPageLifecycle` does nothing outside WebGL players.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `WebGlBeaconTransport` | Sealed class, `IAnalyticsTransport` | Beacon/keepalive POST. `IsSupported`, `CreateForCurrentPlatform()` |
| `WebGlPageLifecycle` | MonoBehaviour | `Attach(IAnalyticsService)`: hidden page → `SetForeground(false)`, visible again → `SetForeground(true)` |

## Behaviour worth knowing

- **No response.** A beacon is fire-and-forget. A request the browser accepted counts as delivered (HTTP 204) and is never retried. One it refused (payload over ~64 KB, beacon queue full, and `fetch` unavailable) is reported as a network error, and the service retries it.
- **Validation mode.** `IGa4Config.ValidateOnly` needs to read GA4's response, so use `UnityWebRequestAnalyticsTransport` while validating.
- **CORS.** Bodies are sent as `text/plain` (a CORS "simple" request), so no preflight is needed. GA4 parses the JSON regardless of the content type.
- **Visibility events.** The jslib listens to `visibilitychange` and `pagehide`, and calls `OnPageHidden` / `OnPageVisible` on the `[WebGlPageLifecycle]` GameObject via `SendMessage`. Duplicate notifications are filtered, and `SetForeground` ignores repeats as well.

## JavaScript functions

| Function | C# caller | Notes |
|---|---|---|
| `LongLeapAnalyticsSendBeacon(url, body)` | `WebGlBeaconTransport` | Returns 1 when the browser accepted the request, 0 otherwise |
| `LongLeapAnalyticsWatchVisibility(gameObjectName)` | `WebGlPageLifecycle.Attach` | Registers the visibility listeners |
