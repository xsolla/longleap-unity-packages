# LongLeap Analytics

Game events to GA4 through the Measurement Protocol. Events are validated on the client, snapshotted at send time, queued, and delivered in batches by a pluggable backend, with retry and backoff.

## Vocabulary

**Event**:
What the game sends: an `IAnalyticsEvent` with a `Name` (`EventName`) and `Params`. It is usually a subclass of `AnalyticsEventBase`, or an ad-hoc `AnalyticsEvent`. The service only reads it and never writes back.

**EventName**:
A typed, GA4-valid event name. Invalid input logs an error and yields `EventName.None` (`IsValid == false`); the service drops such events. `default(EventName)` is `None`.

**Envelope** (`AnalyticsEnvelope`):
The service's snapshot of one event at `Send` time: copied and normalized params, plus `TimestampMicros`, `SessionId`, `UserId` and `UserProperties`. Backends see only envelopes. Avoid "payload" for this: payload means the GA4 JSON request.

**Payload**:
The GA4 JSON request body built by `Ga4PayloadBuilder`. It holds up to 25 envelopes sharing the same `user_id` and user properties.

**Service params**:
`session_id` and `engagement_time_msec`. The service stamps them on every envelope. Game events can't set them.

**Reserved slots**:
5 of GA4's 25 params per event are kept for service params and experiment tags. Game events get 20 (`Ga4Rules.MaxCustomParams`).

**Enricher** (`IAnalyticsEnricher`):
Adds params to every envelope at send time (`TrySetParam`, which never overwrites game params). `ExperimentEnricher` is the built-in one.

**Experiment tag**:
`exp_{experimentId} = variantIndex`, added by `ExperimentEnricher` from an `IExperimentInfoProvider`.

**Backend** (`IAnalyticsBackend`):
Delivers a batch and reports a `SendResult`. `Ga4Backend`, `ConsoleAnalyticsBackend` and `NullAnalyticsBackend` are built in.

**Transport** (`IAnalyticsTransport`):
Moves one HTTP POST for `Ga4Backend`. `UnityWebRequestAnalyticsTransport` is the default; `WebGlBeaconTransport` lives in `com.longleap.platform.webgl`.

**Handled**:
An event the backend is done with: delivered, or dropped for good. `SendResult.RetryLater(handledCount, …)` sends everything after the handled prefix back to the queue.

**Flush**:
Hands the whole queue to the backend. Triggered by the batch size, `Update` after the flush interval, `SetForeground(false)`, an explicit `Flush`/`FlushAsync`, or `Dispose`. One delivery runs at a time.

**Backoff**:
After a failed delivery, automatic flushes pause for `FlushIntervalSeconds × 2^(failures−1)`, capped at `MaxRetryDelaySeconds`. Explicit flushes ignore it.

**Session**:
Ends after `SessionTimeout` of inactivity (no events, or time in background). `SessionId` is the session start time in Unix seconds.

**Engagement time**:
Foreground time since the previous event, at least 1 ms. Background time is excluded once the app reports it via `SetForeground`.

**Collection enabled** (consent):
While off, `Send` is ignored and the queue is cleared.

**Client ID**:
A random per-installation ID (`AnalyticsClientId`), stored in `IPlayerPrefs`. It is GA4's `client_id`. It doesn't belong to `IGa4Config`, which describes the project, not the installation.

## Invariants

- The service never mutates an `IAnalyticsEvent`.
- Envelopes reaching a backend carry at most 25 params, all with valid names and string/long/double values.
- Event order is kept across retries: retried events go back to the front of the queue.
- The queue never exceeds `MaxQueueSize`; overflow drops the oldest event.
- Nothing in the package throws at game code for bad analytics input. Configuration errors (constructor arguments, options) do throw.
- Main thread only.

## Deliberate limits

- The queue is not persisted. Events still queued when the process dies are lost; the flush on going to background narrows that window.
- No ecommerce `items` arrays and no nested params: values are flat strings and numbers.
- No consent mode signals (`ad_user_data`, `ad_personalization`). With collection off, nothing is sent at all.
- No fan-out to several backends from one service. Create one service per backend if you need that, so each keeps its own retry state.

## Relationships

- **Analytics → Core**: `IExperimentInfoProvider` and `AbExperimentInfo` for experiment tags, `IPlayerPrefs` for the client ID.
- **RemoteConfig → Core**: `RemoteConfigService` implements `IExperimentInfoProvider`. Analytics and RemoteConfig don't reference each other; the game wires them with `new ExperimentEnricher(remoteConfig)`.
- **Platform.WebGL → Analytics**: provides `WebGlBeaconTransport` (an `IAnalyticsTransport`) and `WebGlPageLifecycle`.
