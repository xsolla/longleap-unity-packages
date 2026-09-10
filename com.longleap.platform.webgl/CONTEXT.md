# LongLeap Platform WebGL

WebGL-specific platform implementations.

**Ga4WebGlBackend** — Implements `IAnalyticsBackend`. Sends GA4 events via JavaScript interop (`XMLHttpRequest`). Conditionally compiled: `#if UNITY_WEBGL && !UNITY_EDITOR`. Used as an alternative to `Ga4MeasurementBackend` (UnityWebRequest) on WebGL builds.

## Relationships

- **Platform.WebGL → Analytics**: Implements `IAnalyticsBackend`.
- **Platform.WebGL → Core**: Uses `IGa4Config`.
