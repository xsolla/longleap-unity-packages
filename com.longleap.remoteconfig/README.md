# LongLeap Remote Config

Fetches typed game parameters from Firebase Remote Config REST API. No Firebase SDK — pure `UnityWebRequest` calls. Parameters are cached locally in PlayerPrefs and served via inline-default getters. A/B experiment info is auto-appended to GA4 analytics events.

## Package Dependencies

|Package|Version|Role|
|---|---|---|
|`com.longleap.core`|0.1.0|Required. Provides `IPlayerPrefs`, `IExperimentInfoProvider`, `AbExperimentInfo`
|`com.unity.nuget.newtonsoft-json`|3.0.2|Required. JSON caching (PlayerPrefs blob), deserialization
|`com.longleap.firebase`|0.1.0|Optional. Production backend (`FirebaseRemoteConfigBackend`). Not needed for tests or custom backends

## Firebase Prerequisites

The backend talks to two REST APIs. Both must be enabled for your Firebase project.

### 1. Enable APIs in Google Cloud Console

Open the [API Library](https://console.cloud.google.com/apis/library) for your Firebase project and enable:

|API|Purpose|
|---|---|
|**Firebase Installations API**|Registers a device FID (Firebase Installation ID)|
|**Firebase Remote Config API**|Fetches published parameter key/value pairs|

### 2. Fill FirebaseConfigSO fields

Edit `Assets/Resources/Backend/FirebaseConfig.asset` in the Inspector. Three fields under the **Remote Config** header must be set:

|Field|Where to find it|
|---|---|
|`GoogleAppId`|Firebase Console → Project Settings → Your Apps → App ID. Looks like `"1:123456789:android:abc123def456"`|
|`ApiKey`|Firebase Console → Project Settings → Web API Key. Already populated in most projects; Remote Config reuses the same key as RTDB|
|`ProjectNumber`|Firebase Console → Project Settings → Project number. Numeric-only, no dashes (e.g. `123456789`)|

`DatabaseUrl` is not used by Remote Config — no changes needed there.

### 3. Add parameters in Firebase Console

1. Go to **Firebase Console → Remote Config**
2. Click "Add parameter" and enter a key (e.g. `enemy_health_multiplier`)
3. Set a default value
4. Optionally add conditional values for A/B tests or targeting
5. Click **Publish changes** — parameters are not served until published

### 4. (Optional) Create A/B experiments

1. Go to **Firebase Console → Remote Config → A/B Testing**
2. Create an experiment targeting one or more parameters
3. Each variant gets an index (0, 1, 2, …)
4. When the experiment is active and analytics has an `ExperimentEnricher` for this service, every event gets `exp_{experimentId}={variantIndex}`

## Public API

### This package (`LongLeap.RemoteConfig`)

|Type|Namespace|Kind|Purpose|
|---|---|---|---|
|`IRemoteConfigService`|`LongLeap.RemoteConfig`|Interface|`FetchAsync()`, `GetString(key, default)`, `GetInt(key, default)`, `GetFloat(key, default)`, `GetBool(key, default)`. Inherits `IExperimentInfoProvider`|
|`RemoteConfigService`|`LongLeap.RemoteConfig`|Class (sealed)|Implementation. Loads `rc_cache` from PlayerPrefs on construction. Fetches from backend via `FetchAsync()`. `IDisposable`|
|`IRemoteConfigProvider`|`LongLeap.RemoteConfig`|Interface|Provider seam: `Task<RemoteConfigFetchResult> FetchAsync()`|
|`RemoteConfigFetchResult`|`LongLeap.RemoteConfig`|Class|`Parameters` (Dict), `Experiments` (List of `AbExperimentInfo`), `IsSuccess` (bool)|
|`NullRemoteConfigBackend`|`LongLeap.RemoteConfig`|Class (sealed)|No-op backend. Completes synchronously with `IsSuccess=false` — game falls through to cached values or inline defaults|
|`InMemoryPlayerPrefs`|`LongLeap.RemoteConfig.Storage`|Class (sealed)|In-memory `IPlayerPrefs` implementation for tests. No persistence — `Save()` is a no-op|

### `com.longleap.core` (dependency)

|Type|Namespace|Kind|Purpose|
|---|---|---|---|
|`IPlayerPrefs`|`LongLeap.Core`|Interface|PlayerPrefs abstraction: `GetString/Int/Float`, `SetString/Int/Float`, `HasKey`, `DeleteKey`, `Save`|
|`IExperimentInfoProvider`|`LongLeap.Core`|Interface|Exposes `ActiveExperiments` (`IReadOnlyList<AbExperimentInfo>`) for analytics auto-tagging|

### `com.longleap.firebase` (optional)

|Type|Namespace|Kind|Purpose|
|---|---|---|---|
|`FirebaseRemoteConfigBackend`|`LongLeap.Firebase`|Class (sealed)|Production backend. FID registration → REST fetch. Two constructors (see below)|

#### `FirebaseRemoteConfigBackend` constructors

```csharp
// Production: 2-arg constructor (UnityWebRequest internally)
public FirebaseRemoteConfigBackend(FirebaseConfigSO config, IPlayerPrefs playerPrefs)

// Testable: 3-arg constructor with injectable HTTP handler
public FirebaseRemoteConfigBackend(
    FirebaseConfigSO config,
    IPlayerPrefs playerPrefs,
    Func<string, string, string, Task<string>> httpHandler)
```

The 3-arg constructor accepts `httpHandler(string method, string url, string body) → Task<string>` for unit testing — swap in a mock HTTP handler without touching `UnityWebRequest`.

## Initialization

> **Note:** `GameBootstrapper` and `RemoteConfigBootstrapper` are game-level integration classes living in the main game assembly (`Assets/Scripts/`), **not** in this package. They are documented here for context on production wiring.

### Via GameBootstrapper (production path)

1. Add a `RemoteConfigBootstrapper` component to the `GameBootstrapper` GameObject
2. Assign it to the `Remote Config Bootstrapper` field in the GameBootstrapper Inspector
3. At startup, `GameBootstrapper.Start()` calls `remoteConfigBootstrapper.InitializeAsync(config, playerPrefs)` with a 15-second timeout

The bootstrapper:
- Validates `GoogleAppId`, `ApiKey`, `ProjectNumber` — logs fatal error and stops if any are missing
- Creates a `RemoteConfigService` instance
- Calls `FetchAsync()` with timeout polling; on timeout, disposes the service and continues with cache/defaults
- Assigns `Service` (public property, exposed as `IRemoteConfigService`)
- The GameBootstrapper then wires the service into analytics: `analytics.AddEnricher(new ExperimentEnricher(service))`

Access from game code:
```csharp
var multiplier = GameBootstrapper.Instance.RemoteConfig.GetFloat("enemy_health_multiplier", 1.0f);
```

### Direct construction (for tests or custom init)

```csharp
var prefs = new InMemoryPlayerPrefs();
var backend = new FirebaseRemoteConfigBackend(config, prefs);
var service = new RemoteConfigService(backend, prefs);
await service.FetchAsync();
```

You can also pass a mock `IRemoteConfigProvider` or `NullRemoteConfigBackend`.

## Usage

### Reading parameters

```csharp
float multiplier = config.GetFloat("enemy_health_multiplier", 1.0f);
int maxEnemies = config.GetInt("max_enemies", 10);
bool debugMode = config.GetBool("debug_mode", false);
string welcomeMsg = config.GetString("welcome_message", "Hello");
```

Each getter:
1. Returns the value from the last successful fetch (or cache) if the key exists
2. If the key is missing, logs a **one-time** warning then returns the inline default
3. If the value can't be parsed to the requested type (e.g. `GetFloat` on `"banana"`), logs a warning and returns the inline default
4. After `Dispose()`, all getters return the inline default silently

### Refetching (e.g. scene reload)

`SceneManager.LoadScene` triggers a fresh `GameBootstrapper.Start()` which calls `FetchAsync()` again. No manual refetch needed.

### Type coercion for booleans

In Firebase Console, booleans are stored as strings `"true"` or `"false"`. `GetBool` also accepts `"1"` and `"0"` as valid `true`/`false` values.

## Internals

### Data flow

```
Firebase Remote Config REST API
 ↓ POST (FID registration, fetch)
FirebaseRemoteConfigBackend
  → RemoteConfigFetchResult
RemoteConfigService._parameters
  → GetFloat/GetInt/GetBool/GetString
Game code
```

### Cache

Stored in PlayerPrefs under key `rc_cache` as a JSON blob of `Dictionary<string, string>`. Loaded on `RemoteConfigService` construction. Written on every successful `FetchAsync()`. Corrupted JSON is caught, logged as a warning, and discarded — getters fall back to inline defaults.

### FID lifecycle

The backend registers a Firebase Installation ID (FID) on first use via the Firebase Installations REST API. The FID is persisted in PlayerPrefs (`rc_fid_data`) and reused for every subsequent Remote Config fetch. No auth tokens are generated — the `appInstanceId` alone is sufficient for the Remote Config fetch endpoint.

### Request deduplication

Concurrent calls to `FetchAsync()` are collapsed — the second caller awaits the first. No duplicate network requests.

### Experiment auto-tagging

`RemoteConfigService` implements `IExperimentInfoProvider`. Register `new ExperimentEnricher(remoteConfigService)` on the analytics service once; from then on every sent event gets `exp_{id}={variantIndex}` from the current `ActiveExperiments`. See `com.longleap.analytics`.

### Timeout

`RemoteConfigBootstrapper` polls `FetchAsync()` for 15 seconds. On timeout, the service is disposed — late-arriving fetch results are discarded. The game continues with cached values or inline defaults.