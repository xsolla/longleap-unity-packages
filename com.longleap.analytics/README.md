# LongLeap Analytics

Sends game events to Google Analytics 4 through the [Measurement Protocol](https://developers.google.com/analytics/devguides/collection/protocol/ga4), with no Firebase or gtag SDK. It:

- validates events against GA4 rules when you build them;
- batches events, retries with backoff, and keeps the queue bounded;
- tracks sessions and engagement time the way GA4 expects;
- sends device and location data;
- offers a consent switch, `user_id` and user properties;
- tags events with active A/B experiments.

Backends (GA4, console, null) and HTTP transports (UnityWebRequest, WebGL beacon) are pluggable.

## Package dependencies

| Package | Role |
|---|---|
| `com.longleap.core` | `IExperimentInfoProvider` / `AbExperimentInfo` for experiment tagging, `IPlayerPrefs` for the client ID and `UnityPlayerPrefsAdapter` (its `PlayerPrefs` implementation, used in the quick start) |
| `com.unity.nuget.newtonsoft-json` | Request serialization |
| `com.unity.modules.unitywebrequest` | Default HTTP transport |
| `com.longleap.platform.webgl` (optional) | `WebGlBeaconTransport` and `WebGlPageLifecycle` for WebGL builds |

## GA4 setup

1. In GA4 open **Admin → Data streams** and pick (or create) a stream. Copy the **Measurement ID** (`G-XXXXXXXXXX`).
2. In the same stream open **Measurement Protocol API secrets** → **Create**. Copy the secret.
3. In Unity choose **Assets → Create → LongLeap Services → GA4 Config** and fill in both values. Keep the default endpoint, or use `https://region1.google-analytics.com/mp/collect` to keep data in the EU.

The API secret ships inside the build and can be extracted. Treat it as public, and rotate it if the property starts receiving junk traffic.

## Quick start

```csharp
using LongLeap.Analytics;
using LongLeap.Core;             // UnityPlayerPrefsAdapter
using LongLeap.Platform.WebGL;   // optional, WebGL builds

IAnalyticsBackend backend;
if (ga4ConfigAsset.IsConfigured)
{
    var settings = ga4ConfigAsset.ToSettings();
    var clientId = AnalyticsClientId.GetOrCreate(new UnityPlayerPrefsAdapter());
    backend = new Ga4Backend(settings, clientId, WebGlBeaconTransport.CreateForCurrentPlatform(settings))
    {
        Device = Ga4DeviceInfo.FromCurrentPlatform(),
    };
}
else
{
    backend = new ConsoleAnalyticsBackend();   // not configured yet: log instead
}

var analytics = new AnalyticsService(backend, new AnalyticsOptions
{
    CollectionEnabled = playerConsented,                    // consent first
    BackgroundEvent = new EventName("app_paused"),         // optional, see "Engagement"
});

analytics.AddEnricher(new ExperimentEnricher(remoteConfigService)); // optional A/B tags
AnalyticsFlushDriver.Attach(analytics);                     // timer, pause, quit
WebGlPageLifecycle.Attach(analytics);                       // WebGL: flush on tab hide/close

analytics.Send(new AnalyticsEvent("tutorial_completed").With("step_count", 7));
```

Dispose the service when the game shuts down. `Dispose` makes one last delivery attempt.

Without `com.longleap.platform.webgl`, omit the transport argument and the `WebGlPageLifecycle` line. `Ga4Backend` then uses `UnityWebRequestAnalyticsTransport`.

## Defining events

For events sent from more than one place, use a typed class. The name and params then live in one spot:

```csharp
public sealed class LevelCompleted : AnalyticsEventBase
{
    private static readonly EventName EventId = new EventName("level_completed");

    public LevelCompleted(int level, double seconds, bool perfect) : base(EventId)
    {
        SetParam("level", level);          // integer types -> number
        SetParam("duration_sec", seconds); // float/double -> number
        SetParam("perfect", perfect);      // bool -> "true"/"false"
    }
}

analytics.Send(new LevelCompleted(3, 41.2, true));
```

Don't name the static field `Name`: it would hide the inherited `Name` property, which triggers compiler warning CS0108.

For one-offs, use `new AnalyticsEvent("name").With(key, value)`.

The service copies an event when it is sent, so you can reuse or change one instance between `Send` calls.

## Rules enforced on the client

GA4 answers `2xx` even for events it then discards, so the package checks the rules itself and logs the reason.

| Rule | Limit | On violation |
|---|---|---|
| Event name | Letter first, then `[A-Za-z0-9_]`, ≤ 40 chars. Must not start with `google_`, `ga_` or `firebase_`, and must not be a reserved name (`session_start`, `first_visit`, `app_install`, …) | `EventName` logs an error and becomes `None`; the service drops the event |
| Param name | Same character rules, ≤ 40 chars | Param skipped, warning |
| Param value | string (cut to 100 chars, never splitting an emoji), integer, floating point, or bool (sent as a string) | Unsupported type, null, NaN, infinity, or a `ulong` above `long.MaxValue`: param skipped, warning |
| Params per event | 20 set by the game; 5 more reserved for `session_id`, `engagement_time_msec` and experiment tags (GA4 maximum: 25) | Param skipped, warning |
| `user_id` | ≤ 256 chars | Ignored, warning |
| User properties | ≤ 25 properties; name ≤ 24 chars and not reserved; value cut to 36 chars | Ignored or cut, warning |
| Request body | < 130 kB (60 kB with the WebGL beacon) | Requests are split to fit |
| Event age | ≤ 72 h (`AnalyticsOptions.MaxEventAge`) | Dropped before sending, warning |

The constants are in `Ga4Rules`.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `IAnalyticsService` / `AnalyticsService` | Interface / sealed class | Queue, sessions, consent, user data, flushing. Main thread only |
| `AnalyticsOptions` | Class | `BatchSize` (10), `FlushIntervalSeconds` (5), `MaxQueueSize` (500), `MaxAttempts` (5), `MaxEventAge` (72 h), `MaxRetryDelaySeconds` (300), `SessionTimeout` (30 min), `CollectionEnabled` (true), `BackgroundEvent` (none), `Clock` |
| `IAnalyticsEvent` | Interface | `Name` + `Params`. Read-only from the service's point of view |
| `AnalyticsEventBase` | Abstract class | Base for typed events; validating `protected SetParam` overloads, plus `RemoveParam` |
| `AnalyticsEvent` | Sealed class | Ad-hoc event with a fluent `With(key, value)` |
| `EventName` | Readonly struct | Validated event name; `None`, `IsValid`, `TryCreate` |
| `AnalyticsEnvelope` | Sealed class | The service's snapshot of an event: name, params, `TimestampMicros`, `SessionId`, `UserId`, `UserProperties`. Backends receive these |
| `IAnalyticsEnricher` | Interface | Adds params to every event at send time |
| `ExperimentEnricher` | Sealed class | Adds `exp_{experimentId} = variantIndex` from an `IExperimentInfoProvider` |
| `IAnalyticsBackend` | Interface | `Task<SendResult> SendAsync(IReadOnlyList<AnalyticsEnvelope>)` |
| `SendResult` | Readonly struct | `Complete`, or `RetryLater(handledCount, error, attemptedCount)` |
| `Ga4Backend` | Sealed class | GA4 Measurement Protocol backend. `Device`, `UserLocation`, `MaxRequestBytes` |
| `Ga4Settings` | Sealed class | `MeasurementId`, `ApiSecret`, `EndpointUrl`, `ValidateOnly` |
| `Ga4ConfigSO` | ScriptableObject | Settings asset: `IsConfigured`, `ToSettings()` |
| `Ga4DeviceInfo` | Sealed class | GA4 `device` object; `FromCurrentPlatform()` |
| `Ga4UserLocation` | Sealed class | GA4 `user_location` object (country, region, city, …) |
| `Ga4PayloadBuilder` | Static class | `BuildUrl`, `BuildRequests`: the GA4 wire format |
| `Ga4Rules` | Static class | GA4 limits and name validation, plus `Truncate` |
| `ConsoleAnalyticsBackend` | Sealed class | Logs events to the console instead of sending them |
| `NullAnalyticsBackend` | Sealed class | Discards everything |
| `IAnalyticsTransport` / `AnalyticsHttpResponse` | Interface / struct | One HTTP POST |
| `IAnalyticsTransportLimits` | Interface | Optional on a transport: `MaxBodyBytes` |
| `UnityWebRequestAnalyticsTransport` | Sealed class | Default transport |
| `AnalyticsClientId` | Static class | `GetOrCreate(prefs)` / `Reset(prefs)` for a persistent random client ID |
| `AnalyticsFlushDriver` | MonoBehaviour | `Attach(service)` (one per service), `TryGet`, `Detach`, `DisableFocusTracking(service)` |

## How it works

### Send

```
Send(event)
  → dropped if disposed, consent off, or the name is invalid
  → session check: more than SessionTimeout since the last activity → new session
  → AnalyticsEnvelope: copy of the params (validated), timestamp, session, user_id, user properties
  → session_id, engagement_time_msec
  → enrichers, in registration order
  → queue (when full, the oldest event is dropped)
  → flush when the queue reaches BatchSize, unless backing off after a failure
```

### Flush and retry

A flush starts when:
- the queue reaches `BatchSize`;
- `Update` is called and `FlushIntervalSeconds` have passed;
- `SetForeground(false)` is called;
- `Flush` / `FlushAsync` is called (these ignore the backoff);
- the service is disposed.

Only one delivery runs at a time. A flush requested during a delivery runs right after it, if that delivery succeeded. After a failure, the backoff decides when to try again. The task returned by `FlushAsync` completes once that extra round is over. It never faults, so check `PendingCount` to see whether everything went out.

One run makes at most 4 deliveries. That way a backend which itself produces events (for example, a log forwarder feeding `Send`) can't loop forever.

The backend reports `SendResult`:

- **Complete**: every event was delivered, or dropped for good. For example, GA4 rejected it with HTTP 4xx; `Ga4Backend` logs that as an error.
- **RetryLater(handled, error, attempted)**:
  - The first `handled` events are done.
  - The rest go back to the front of the queue in their original order.
  - Automatic flushes pause for `FlushIntervalSeconds × 2^(failures−1)`, up to `MaxRetryDelaySeconds`.
  - The `attempted` events reached the server and got a failure answer. Each such answer uses up one of `MaxAttempts`, and an event is dropped once they are spent.
  - Events that never got an answer, because of no network, a timeout, or never being tried, are retried without limit. An offline session therefore waits until it can be sent. It is bounded only by `MaxQueueSize` and `MaxEventAge` (GA4 ignores events older than 72 h anyway).

`Ga4Backend` classifies failures like this:
- network error or timeout → RetryLater, no attempt used;
- HTTP 408, 429 or 5xx → RetryLater, an attempt used for that request's events;
- any other non-2xx → dropped and logged.

The queue is in memory only. Events still queued when the process is killed are lost. On mobile and WebGL, the flush on going to background is what saves them, so attach `AnalyticsFlushDriver` (and `WebGlPageLifecycle` on WebGL).

### Sessions and engagement

- `session_id` is the session start time in Unix seconds. A new session starts after `SessionTimeout` (default 30 min) of inactivity: no events, or time spent in background.
- `engagement_time_msec` is the time in foreground since the previous event, with a minimum of 1 ms. GA4 needs both params to count the user as active. Background time is excluded once the app reports it through `SetForeground` (the flush driver does this).
- Time after the last event is only reported with the next event, so an app killed in background never reports it. Set `AnalyticsOptions.BackgroundEvent` to a custom name such as `app_paused`: the service then sends that event, carrying the engagement time, every time the app goes to background. GA4 reserves `user_engagement` for its own tags.

### Device and location

GA4 doesn't derive device or geo data for Measurement Protocol traffic. Without them, reports show platform, OS, language, screen resolution, country and city as "(not set)".

- **Device:** set `Ga4Backend.Device = Ga4DeviceInfo.FromCurrentPlatform()`. It fills category, language, screen resolution, operating system and version, and, on native mobile, brand and model. Unity can't see the browser name on WebGL; set `Browser` yourself if you need it.
- **Location:** if your backend knows the player's country, set `Ga4Backend.UserLocation = new Ga4UserLocation { CountryId = "DE" }`. The package doesn't guess location from the device locale, which is often wrong.

### Consent

`SetCollectionEnabled(false)` clears the queue and ignores `Send` until consent is given again. A request already in flight still completes; if it fails, its events are discarded instead of retried. When consent is given, engagement time starts counting from that moment.

If a player withdraws consent for good, also call `AnalyticsClientId.Reset(prefs)`. A later opt-in then appears in GA4 as a new user who can't be linked to the old one.

### Users

- `SetUserId(id)` sets GA4's `user_id` for events sent afterwards. Use a stable account ID (for example, after Xsolla ID sign-in). Never use an email or a name.
- `SetUserProperty(name, value)` sets a user property for events sent afterwards. Pass `null` to remove one.

Both values are sent at the request level. `Ga4PayloadBuilder` starts a new request whenever either changes inside a batch.

### Player loop

`AnalyticsFlushDriver.Attach(service)` creates one hidden `DontDestroyOnLoad` object per service and returns the existing one when called again.

- **Native platforms:** it forwards `OnApplicationPause`.
- **WebGL:** it forwards window focus, until `WebGlPageLifecycle` takes over with page visibility. Visibility is the better signal: focus is also lost when the player clicks outside the canvas, on a portal page for example.

`Detach()` removes the driver; the service is never disposed by it.

### Debugging

- Set `ValidateOnly` on the GA4 settings. Requests then go to `/debug/mp/collect`; GA4 records nothing and returns validation messages, which `Ga4Backend` logs as warnings. This needs a transport that can read responses. `WebGlBeaconTransport.CreateForCurrentPlatform(settings)` picks one automatically.
- Swap in `ConsoleAnalyticsBackend` to see every event in the console without sending anything.

### Threading

The service is main-thread only. The `UnityWebRequest` transport completes on the main thread. A custom backend or transport must not resume on a worker thread.

## Writing a backend

Implement `IAnalyticsBackend`:
- Return `SendResult.Complete` once you are done with every event.
- For a transient failure, return `SendResult.RetryLater(handledCount, error, attemptedCount)`. Count only the events the destination actually answered with a failure.
- Don't throw: an exception is treated as `RetryLater(0, …, all)` for the whole batch.

To send the GA4 format over a different channel, don't write a backend. Implement `IAnalyticsTransport` (plus `IAnalyticsTransportLimits` if it has a size limit) and pass it to `Ga4Backend`.

## Testing

The EditMode tests are in `Tests/EditMode` and make no network calls:
- `Ga4Backend` runs against a fake transport;
- the service uses a manual clock (`AnalyticsOptions.Clock`);
- in-flight deliveries are driven by hand.

To run them in a project, add `com.longleap.analytics` to `testables` in `Packages/manifest.json`.

WebGL behaviour can only be checked in a browser; see the checklist in `com.longleap.platform.webgl/README.md`.
