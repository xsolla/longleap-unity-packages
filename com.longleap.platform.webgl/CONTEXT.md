# LongLeap Platform WebGL

WebGL-specific implementations of LongLeap interfaces.

**WebGlBeaconTransport**:
Implements `IAnalyticsTransport` with `navigator.sendBeacon`, falling back to `fetch` with `keepalive`. Requests survive tab close. It can't read responses: accepted means delivered.

**WebGlPageLifecycle**:
MonoBehaviour that maps the browser's page visibility (`visibilitychange`, `pagehide`) to `IAnalyticsService.SetForeground`. Hidden means background, which flushes the queue.

**LongLeapAnalytics.jslib**:
The JavaScript side of both. Function names are prefixed with `LongLeapAnalytics` to avoid clashes with other plugins.

## Relationships

- **Platform.WebGL → Analytics**: implements `IAnalyticsTransport` and drives `IAnalyticsService`. The GA4 payload format stays in `Ga4PayloadBuilder` in analytics; this package only moves bytes.
