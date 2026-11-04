# LongLeap Platform WebGL

WebGL-specific implementations of LongLeap interfaces.

**WebGlBeaconTransport**:
Implements `IAnalyticsTransport` with `navigator.sendBeacon`. Requests survive tab close. Accepted by the browser means delivered; refused means a network error, which is retried. Bodies are capped at 60 kB through `IAnalyticsTransportLimits`.

**WebGlPageLifecycle**:
MonoBehaviour that maps the browser's page visibility (`visibilitychange`, `pagehide`, `pageshow`) to `IAnalyticsService.SetForeground`. Hidden means background, which flushes the queue. Attaching it switches off `AnalyticsFlushDriver`'s focus tracking for the same service.

**LongLeapAnalytics.jslib**:
The JavaScript side of both. Function names are prefixed with `LongLeapAnalytics` to avoid clashes with other plugins; listeners are kept per GameObject and removed on destroy.

## Relationships

- **Platform.WebGL → Analytics**: implements `IAnalyticsTransport`/`IAnalyticsTransportLimits` and drives `IAnalyticsService`. The GA4 payload format stays in `Ga4PayloadBuilder` in analytics; this package only moves bytes.
