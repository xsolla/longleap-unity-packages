# LongLeap Platform WebGL

WebGL-only GA4 analytics backend. Sends events via JavaScript interop instead of `UnityWebRequest` — avoids blocking the WebGL main thread.

## Prerequisites

The consuming project must define the following JavaScript function in its WebGL template or a `.jslib` plugin:

```javascript
function SendGa4Beacon(url, body) { ... }
```

Without this function, the backend compiles but will fail at runtime with a missing-function error.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `Ga4WebGlBackend` | Class (sealed) | `IAnalyticsBackend` implementation guarded by `#if UNITY_WEBGL && !UNITY_EDITOR`. Constructor: `(IGa4Config config)`. Splits batches into chunks of 25, serializes to GA4 JSON, sends via `SendGa4Beacon`. |

Consumed interfaces (from `LongLeap.Analytics`):

| Interface | Members |
|---|---|
| `IAnalyticsBackend` | `SendBatch(IReadOnlyList<IAnalyticsEvent>)` |
| `IGa4Config` | `MeasurementId`, `ApiSecret`, `ClientId`, `EndpointUrl` |
| `IAnalyticsEvent` | `EventName`, `Params`, `TimestampMicros`, `SessionId`, `SetParam(key, value)` |

## Initialization

```csharp
IGa4Config config = /* from ScriptableObject or DI */;

#if UNITY_WEBGL && !UNITY_EDITOR
    IAnalyticsBackend backend = new Ga4WebGlBackend(config);
#else
    IAnalyticsBackend backend = new Ga4MeasurementBackend(config);
#endif

var service = new AnalyticsService(
    batchSize: 10,
    flushIntervalSec: 5f,
    backend: backend);
```

## Internals

### SendBatch flow

```
SendBatch(events)
  → Chunk into groups of 25 (GA4 batch limit)
  → Per chunk:
    → Serialize to JSON: {client_id, events: [{name, params, timestamp_micros}]}
    → Inject session_id from IAnalyticsEvent.SessionId if present
    → SendGa4Beacon(endpointUrl, jsonBody)   // JS interop, non-blocking
```

### Platform guard

The entire class is wrapped in `#if UNITY_WEBGL && !UNITY_EDITOR`. On other platforms, `Ga4WebGlBackend` is invisible to the compiler.

### JS interop

`[DllImport("__Internal__")] private static extern void SendGa4Beacon(string url, string body);`

No return value — fire-and-forget. JS side typically uses `navigator.sendBeacon` for reliable delivery on page unload.