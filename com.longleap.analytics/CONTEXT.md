# LongLeap Analytics

Pluggable analytics service with GA4 Measurement Protocol backend. Events are batched and flushed periodically. No internal timer — the caller drives flushing.

**AnalyticsBackend**:
A swappable implementation (`IAnalyticsBackend`). Production: `Ga4MeasurementBackend` (UnityWebRequest POST to GA4). WebGL: `Ga4WebGlBackend` (JavaScript interop, in `com.longleap.platform.webgl`).

**Event**:
An analytics payload implementing `IAnalyticsEvent`. Subclass `AnalyticsEventBase`, pass a validated `EventName` to the base constructor, call `SetParam` for each field. Constraints: param keys `[a-zA-Z_][a-zA-Z0-9_]{0,39}`, max 25 params, string values truncated to 100 chars.

**EventName**:
Typed newtype validated against `^[a-zA-Z][a-zA-Z0-9_]{0,39}$`. Falls back to `"invalid_event"` if invalid. Value equality.

**Batching**:
Two-level: `AnalyticsService` holds a queue, flushes to backend. `Ga4MeasurementBackend` chunks into 25 per HTTP request (GA4 limit).

**Flush**:
No internal timer. Caller drives flushing via coroutine or Update loop. `Flush()` auto-triggers when queue reaches `batchSize`. Reentrancy-guarded.

**Experiment auto-tagging**:
`AnalyticsService` reads `IExperimentInfoProvider.ActiveExperiments` on every `Send()` and appends `exp_{id}={variantIndex}` to params. Wire via `SetExperimentInfoProvider()`.

## Relationships

- **Analytics → Core**: Uses `IGa4Config`, `IExperimentInfoProvider`, `AbExperimentInfo`.
- **Analytics → RemoteConfig**: `RemoteConfigService` implements `IExperimentInfoProvider` for A/B experiment data.
