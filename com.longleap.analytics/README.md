# LongLeap Analytics

Sends game events to Google Analytics 4 through the [Measurement Protocol](https://developers.google.com/analytics/devguides/collection/protocol/ga4), with no Firebase or gtag SDK. It:

- validates events against GA4 rules when you build them;
- batches events, retries with backoff, and keeps the queue bounded;
- tracks sessions and engagement time the way GA4 expects;
- offers a consent switch, `user_id` and user properties;
- tags events with active A/B experiments.

Backends (GA4, console, null) and HTTP transports (UnityWebRequest, WebGL beacon) are pluggable.

## Package dependencies

| Package | Role |
|---|---|
| `com.longleap.core` | `IExperimentInfoProvider` / `AbExperimentInfo` for experiment tagging, `IPlayerPrefs` for the client ID |
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
using LongLeap.Platform.Unity;   // UnityPlayerPrefsAdapter
using LongLeap.Platform.WebGL;   // optional, WebGL builds

IGa4Config config = ga4ConfigAsset;                       // Ga4ConfigSO
var clientId = AnalyticsClientId.GetOrCreate(new UnityPlayerPrefsAdapter());

var backend = new Ga4Backend(config, clientId, WebGlBeaconTransport.CreateForCurrentPlatform());
var analytics = new AnalyticsService(backend, new AnalyticsOptions
{
    CollectionEnabled = playerConsented,                  // consent first
});

analytics.AddEnricher(new ExperimentEnricher(remoteConfigService)); // optional A/B tags
AnalyticsFlushDriver.Attach(analytics);                    // timer, pause, quit
WebGlPageLifecycle.Attach(analytics);                      // WebGL: flush on tab hide/close

analytics.Send(new AnalyticsEvent("tutorial_completed").With("step_count", 7));
```

Dispose the service when the game shuts down. `Dispose` makes one last delivery attempt.

Without `com.longleap.platform.webgl`, omit the transport argument and the `WebGlPageLifecycle` line. `Ga4Backend` then uses `UnityWebRequestAnalyticsTransport`.

## Defining events

For events sent from more than one place, use a typed class. The name and params then live in one spot:

```csharp
public sealed class LevelCompleted : AnalyticsEventBase
{
    private static readonly EventName Name = new EventName("level_completed");

    public LevelCompleted(int level, double seconds, bool perfect) : base(Name)
    {
        SetParam("level", level);          // int/long  -> number
        SetParam("duration_sec", seconds); // float/double -> number
        SetParam("perfect", perfect);      // bool -> "true"/"false"
    }
}

analytics.Send(new LevelCompleted(3, 41.2, true));
```

For one-offs, use `new AnalyticsEvent("name").With(key, value)`.

The service copies an event when it is sent, so you can reuse or change one instance between `Send` calls.

## Rules enforced on the client

GA4 answers `2xx` even for events it then discards, so the package checks the rules itself and logs the reason.

| Rule | Limit | On violation |
|---|---|---|
| Event name | Letter first, then `[A-Za-z0-9_]`, ≤ 40 chars. Must not start with `google_`, `ga_` or `firebase_`, and must not be a reserved name (`session_start`, `first_visit`, …) | `EventName` logs an error and becomes `None`; the service drops the event |
| Param name | Same character rules, ≤ 40 chars | Param skipped, warning |
| Param value | string (cut to 100 chars), integer, floating point, or bool (sent as a string) | Unsupported type, null, NaN or infinity: param skipped, warning |
| Params per event | 20 set by the game; 5 more reserved for `session_id`, `engagement_time_msec` and experiment tags (GA4 maximum: 25) | Param skipped, warning |
| `user_id` | ≤ 256 chars | Ignored, warning |
| User properties | ≤ 25 properties; name ≤ 24 chars and not reserved; value cut to 36 chars | Ignored or cut, warning |

The constants are in `Ga4Rules`.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `IAnalyticsService` / `AnalyticsService` | Interface / sealed class | Queue, sessions, consent, user data, flushing. Main thread only |
| `AnalyticsOptions` | Class | `BatchSize` (10), `FlushIntervalSeconds` (5), `MaxQueueSize` (500), `MaxAttempts` (5), `MaxRetryDelaySeconds` (300), `SessionTimeout` (30 min), `CollectionEnabled` (true), `Clock` |
| `IAnalyticsEvent` | Interface | `Name` + `Params`. Read-only from the service's point of view |
| `AnalyticsEventBase` | Abstract class | Base for typed events; validating `protected SetParam` overloads, plus `RemoveParam` |
| `AnalyticsEvent` | Sealed class | Ad-hoc event with a fluent `With(key, value)` |
| `EventName` | Readonly struct | Validated event name; `None`, `IsValid`, `TryCreate` |
| `AnalyticsEnvelope` | Sealed class | The service's snapshot of an event: name, params, `TimestampMicros`, `SessionId`, `UserId`, `UserProperties`. Backends receive these |
| `IAnalyticsEnricher` | Interface | Adds params to every event at send time |
| `ExperimentEnricher` | Sealed class | Adds `exp_{experimentId} = variantIndex` from an `IExperimentInfoProvider` |
| `IAnalyticsBackend` | Interface | `Task<SendResult> SendAsync(IReadOnlyList<AnalyticsEnvelope>)` |
| `SendResult` | Readonly struct | `Complete`, or `RetryLater(handledCount, error)` |
| `Ga4Backend` | Sealed class | GA4 Measurement Protocol backend |
| `ConsoleAnalyticsBackend` | Sealed class | Logs events to the console instead of sending them |
| `NullAnalyticsBackend` | Sealed class | Discards everything |
| `IGa4Config` / `Ga4ConfigSO` | Interface / ScriptableObject | `MeasurementId`, `ApiSecret`, `EndpointUrl`, `ValidateOnly` |
| `Ga4PayloadBuilder` | Static class | `BuildUrl`, `BuildRequests`: the GA4 wire format |
| `Ga4Rules` | Static class | GA4 limits and name validation |
| `IAnalyticsTransport` / `AnalyticsHttpResponse` | Interface / struct | One HTTP POST |
| `UnityWebRequestAnalyticsTransport` | Sealed class | Default transport |
| `AnalyticsClientId` | Static class | `GetOrCreate(prefs)` / `Reset(prefs)` for a persistent random client ID |
| `AnalyticsFlushDriver` | MonoBehaviour | `Attach(service)`: calls `Update`, and forwards pause/focus/quit to `SetForeground` |

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

Only one delivery runs at a time. A flush requested during a delivery runs right after it.

The backend reports `SendResult`:

- **Complete**: every event was delivered, or dropped for good (for example, GA4 rejected it with HTTP 4xx; `Ga4Backend` logs that as an error).
- **RetryLater(n)**: the first `n` events are done. The rest go back to the front of the queue in their original order. Automatic flushes then pause for `FlushIntervalSeconds × 2^(failures−1)`, up to `MaxRetryDelaySeconds`. An event is dropped after `MaxAttempts` failed attempts.

`Ga4Backend` treats these as transient (RetryLater): network errors, timeouts, and HTTP 408, 429 and 5xx.

The queue is in memory only. Events still queued when the process is killed are lost. On mobile and WebGL, the flush on going to background is what saves them, so attach `AnalyticsFlushDriver` (and `WebGlPageLifecycle` on WebGL).

### Sessions and engagement

- `session_id` is the session start time in Unix seconds. A new session starts after `SessionTimeout` (default 30 min) of inactivity: no events, or time spent in background.
- `engagement_time_msec` is the time in foreground since the previous event, with a minimum of 1 ms. GA4 needs both params to count the user as active. Background time is excluded once the app reports it through `SetForeground` (the flush driver does this).

### Consent

`SetCollectionEnabled(false)` clears the queue and ignores `Send` until consent is given again. Requests already in flight still complete.

If a player withdraws consent for good, also call `AnalyticsClientId.Reset(prefs)`. A later opt-in then appears in GA4 as a new user who can't be linked to the old one.

### Users

- `SetUserId(id)` sets GA4's `user_id` for events sent afterwards. Use a stable account ID (for example, after Xsolla ID sign-in). Never use an email or a name.
- `SetUserProperty(name, value)` sets a user property for events sent afterwards. Pass `null` to remove one.

Both values are sent at the request level. `Ga4PayloadBuilder` starts a new request whenever either changes inside a batch.

### Debugging

- Set `ValidateOnly` on the GA4 config. Requests then go to `/debug/mp/collect`; GA4 records nothing and returns validation messages, which `Ga4Backend` logs as warnings. This needs a transport that can read responses (UnityWebRequest, not the beacon).
- Swap in `ConsoleAnalyticsBackend` to see every event in the console without sending anything.

### Threading

The service is main-thread only. The `UnityWebRequest` transport completes on the main thread. A custom backend or transport must not resume on a worker thread.

## Writing a backend

Implement `IAnalyticsBackend`. Return `SendResult.Complete` once you are done with every event. For a transient failure, return `SendResult.RetryLater(handledCount, error)`. Don't throw: an exception is treated as `RetryLater(0)` for the whole batch.

To send the GA4 format over a different channel, don't write a backend. Implement `IAnalyticsTransport` and pass it to `Ga4Backend`.

## Testing

The EditMode tests are in `Tests/EditMode` and make no network calls. `Ga4Backend` runs against a fake transport, and the service uses a manual clock (`AnalyticsOptions.Clock`).
