# LongLeap Remote Config

Typed remote parameters with inline defaults, a PlayerPrefs cache and A/B experiment info. Backend-agnostic; see README.md for usage.

**IRemoteConfigService / RemoteConfigService**:
Game-facing API. Loads the cache in the constructor; `FetchAsync` asks the provider, caches the result and applies it (or keeps it pending until `Activate()` when `ActivateOnFetch` is off). Concurrent fetches share one request; a caller's token only cancels its own wait; `Dispose` aborts the request. Implements `IExperimentInfoProvider`.

**IRemoteConfigProvider**:
Backend seam. One request per call, full parameter set on success. Implementations: `FirebaseRemoteConfigProvider` (`com.longleap.firebase`), `NullRemoteConfigProvider` (here).

**RemoteConfigFetchResult / RemoteConfigFetchStatus**:
Provider result (`Success(...)` / `Failure`, collections never null) and service outcome (`Success`, `Failure`, `Throttled`, `Canceled`).

**RemoteConfigOptions**:
`CacheKey`, `ActivateOnFetch`, `MinimumFetchInterval`, `WarnOnMissingKeys`, `UtcNow`.

**Cache** (`RemoteConfigCache`, internal):
Versioned JSON in `IPlayerPrefs`: parameters, experiments, fetch time. Experiments are cached so a cached A/B variant is never served untagged. Unreadable or other-version caches are discarded with a warning.

**Parsing** (`RemoteConfigValueParser`, internal):
Culture-invariant, trimmed; whole decimals accepted as integers; non-finite floats rejected; Firebase SDK boolean tokens.

## Relationships

- **RemoteConfig → Core**: `IPlayerPrefs`, `IExperimentInfoProvider`, `AbExperimentInfo`; tests use `InMemoryPlayerPrefs`.
- **Firebase → RemoteConfig**: `FirebaseRemoteConfigProvider` implements `IRemoteConfigProvider`.
- **Analytics → Core ← RemoteConfig**: `ExperimentEnricher` reads `ActiveExperiments` through `IExperimentInfoProvider`; analytics does not reference this package.
