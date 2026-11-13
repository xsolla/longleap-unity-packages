# LongLeap Remote Config

Typed remote game parameters with inline defaults. The service caches the last successful fetch in `IPlayerPrefs`, so a launch starts with the previous session's values, and exposes the player's A/B experiments to analytics.

The package does not talk to any server itself. A provider (`IRemoteConfigProvider`) does the request:

| Provider | Package | Use |
|---|---|---|
| `FirebaseRemoteConfigProvider` | `com.longleap.firebase` | Production. Firebase Remote Config over REST, no Firebase SDK, works on WebGL. Setup: see that package's README |
| `NullRemoteConfigProvider` | this package | Offline builds and tests. Every fetch fails, so cached values or inline defaults are used |
| your own | — | Implement `IRemoteConfigProvider` (see [Custom provider](#custom-provider)) |

## Install (Unity Package Manager)

Unity 6 (6000.0 – 6000.4), WebGL build target. In Unity: **Window → Package Manager → + → Add package from git URL**. Add the packages in this order: a package installed from Git does not pull in other Git packages, so the LongLeap packages it needs go first.

```
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.core
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.remoteconfig
```

Pin a version by adding `#v1.0.0` at the end of each URL. Unity packages this one uses are installed automatically. All packages and their dependencies: [root README](../README.md#install-unity-package-manager).

## Dependencies

| Package | Version | Role |
|---|---|---|
| `com.longleap.core` | 1.0.0 | `IPlayerPrefs`, `IExperimentInfoProvider`, `AbExperimentInfo`; `InMemoryPlayerPrefs` for tests |
| `com.unity.nuget.newtonsoft-json` | 3.0.2 | Cache serialization, `GetJson<T>` |

## Quick start

```csharp
IPlayerPrefs prefs = new UnityPlayerPrefsAdapter();
IRemoteConfigProvider provider = new FirebaseRemoteConfigProvider(firebaseConfig, prefs);
// or: new NullRemoteConfigProvider();

var remoteConfig = new RemoteConfigService(provider, prefs);

// Optional startup timeout: the game continues with cached values or defaults.
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    var status = await remoteConfig.FetchAsync(timeout.Token);
}

// A/B tagging of analytics events (com.longleap.analytics)
analytics.AddEnricher(new ExperimentEnricher(remoteConfig));

float hp = remoteConfig.GetFloat("enemy_health_multiplier", 1.0f);
```

`CancellationTokenSource(TimeSpan)` needs a thread-pool timer. WebGL has no threads, so there, cancel the token yourself from a coroutine or the player loop.

Dispose the service when its owner goes away. Disposing aborts a fetch in flight and drops its result.

## Reading values

```csharp
int maxEnemies   = remoteConfig.GetInt("max_enemies", 10);
long seed        = remoteConfig.GetLong("seed", 0);
double rate      = remoteConfig.GetDouble("drop_rate", 0.05);
bool newShop     = remoteConfig.GetBool("new_shop", false);
string greeting  = remoteConfig.GetString("welcome_message", "Hello");
WaveConfig wave  = remoteConfig.GetJson("wave_1", WaveConfig.Default);

if (remoteConfig.TryGetInt("bonus_coins", out var bonus)) { /* key present and valid */ }
```

| Rule | Behaviour |
|---|---|
| `Get*`, key missing | Returns the default. Logs a warning once per key, but only after values came from the server (successful fetch or loaded cache), and only if `WarnOnMissingKeys` is on |
| `Get*`, value invalid | Returns the default. Logs a warning once per key and type |
| `TryGet*` | Never logs. `false` when missing or invalid |
| Numbers | Culture-invariant, surrounding whitespace ignored. Integers accept whole decimals (`"42.0"`). `NaN`/`Infinity` are rejected |
| Booleans | `true/t/yes/y/on/1` and `false/f/no/n/off/0`, case-insensitive (same as the Firebase SDK) |
| JSON | `GetJson<T>` / `TryGetJson<T>` use Newtonsoft.Json. A JSON `null` counts as invalid |
| After `Dispose()` | Every getter returns its default silently; `ActiveExperiments` is empty |

`Values` is a read-only snapshot of all raw values; `HasKey` checks one key.

## Fetching

`FetchAsync(CancellationToken)` returns a `RemoteConfigFetchStatus`:

| Status | Meaning |
|---|---|
| `Success` | Values fetched and cached (and applied, see [Activation](#activation)) |
| `Failure` | The provider failed or threw. Current values are kept |
| `Throttled` | No request: the last success is younger than `MinimumFetchInterval` |
| `Canceled` | The caller's token was cancelled, or the service is disposed |

- **Shared request.** Calls made while a fetch is in flight join it; the provider is called once.
- **Cancellation** only stops the caller's wait. The request keeps running for other callers and its result is still applied. `Dispose()` is what aborts the request.
- **Success replaces the whole set.** A parameter removed on the server disappears from `Values`.
- `LastFetchStatus` and `LastSuccessfulFetchUtc` report the latest outcome; the success time is restored from the cache.

## Activation

By default a successful fetch applies at once and raises `Updated`. Values that change in the middle of a level can be awkward, so activation can be deferred:

```csharp
var remoteConfig = new RemoteConfigService(provider, prefs, new RemoteConfigOptions { ActivateOnFetch = false });
await remoteConfig.FetchAsync();   // cached; HasPendingValues == true
// ... later, at a safe moment (main menu, between levels):
remoteConfig.Activate();           // applies and raises Updated
```

Without `Activate()`, fetched values apply on the next launch, because the constructor loads the cache. Parameters and experiments always change together.

## Options

| `RemoteConfigOptions` | Default | Purpose |
|---|---|---|
| `CacheKey` | `"rc_cache"` | `IPlayerPrefs` key of the cache. Use one key per Firebase project or environment |
| `ActivateOnFetch` | `true` | `false` defers activation (see above) |
| `MinimumFetchInterval` | `TimeSpan.Zero` | Throttles fetches, also across launches. `Zero` turns throttling off |
| `WarnOnMissingKeys` | `true` | Missing-key warnings |
| `UtcNow` | `DateTime.UtcNow` | Clock, replaceable in tests |

## A/B experiments

`RemoteConfigService` implements `IExperimentInfoProvider`. `ActiveExperiments` lists the experiments of the active values. `ExperimentEnricher` (`com.longleap.analytics`) adds `exp_{experimentId}={variantIndex}` to every analytics event. Experiments are cached with the parameters, so events sent before the first fetch of a session are already tagged.

## Cache

One JSON string in `IPlayerPrefs` under `CacheKey`, written on every successful fetch:

```json
{"v":1,"fetchedAtUnixMs":1762250000000,"parameters":{"max_enemies":"12"},"experiments":[{"id":"exp_01","variant":1}]}
```

A cache that cannot be read (corrupted, or a different format version) is discarded with a warning; defaults are used until the next successful fetch.

## Custom provider

```csharp
public sealed class MyProvider : IRemoteConfigProvider
{
    public async Task<RemoteConfigFetchResult> FetchAsync(CancellationToken cancellationToken = default)
    {
        var values = await LoadFromMyServerAsync(cancellationToken); // null on error
        return values != null
            ? RemoteConfigFetchResult.Success(values, experiments: null)
            : RemoteConfigFetchResult.Failure;
    }
}
```

Return the complete parameter set on success. Report errors with `RemoteConfigFetchResult.Failure` instead of throwing; the service treats an exception as a failure and logs a warning. The token is cancelled when the service is disposed.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `IRemoteConfigService` | Interface (`IExperimentInfoProvider`, `IDisposable`) | Getters, `FetchAsync`, `Activate`, `Updated`, fetch status |
| `RemoteConfigService` | Sealed class | Default implementation: `(IRemoteConfigProvider, IPlayerPrefs, RemoteConfigOptions = null)` |
| `RemoteConfigOptions` | Sealed class | Settings, read once in the constructor |
| `RemoteConfigFetchStatus` | Enum | `Success`, `Failure`, `Throttled`, `Canceled` |
| `IRemoteConfigProvider` | Interface | Backend seam: `FetchAsync(CancellationToken)` |
| `RemoteConfigFetchResult` | Sealed class | `Success(parameters, experiments)` / `Failure`; collections never null |
| `NullRemoteConfigProvider` | Sealed class | Offline provider; always `Failure`, completes synchronously |

## Threading

Not thread-safe. Call the service from Unity's main thread; `Updated` is raised there too. An exception in an `Updated` handler is logged and does not stop the other handlers.

## Tests

EditMode tests in `Tests/EditMode` (assembly `LongLeap.RemoteConfig.Tests`) run without a network, using a fake provider and `InMemoryPlayerPrefs`.
