# LongLeap Remote Config

Fetches typed game parameters from Firebase Remote Config REST API. No Firebase SDK — pure `UnityWebRequest` calls. Parameters are cached locally in PlayerPrefs and served via inline-default getters. A/B experiment info is auto-appended to GA4 analytics events.

**IRemoteConfigService**:
Interface: `FetchAsync()`, `GetString(key, default)`, `GetInt`, `GetFloat`, `GetBool`. Inherits `IExperimentInfoProvider`.

**RemoteConfigService**:
Implementation. Loads `rc_cache` from PlayerPrefs on construction. Fetches from backend via `FetchAsync()`. `IDisposable`. Deduplicates concurrent fetch calls.

**IRemoteConfigProvider**:
Provider seam: `Task<RemoteConfigFetchResult> FetchAsync()`. Implementations: `FirebaseRemoteConfigBackend` (production, in `com.longleap.firebase`), `NullRemoteConfigBackend` (no-op, in this package).

**NullRemoteConfigBackend**:
No-op backend. Every fetch completes synchronously with `IsSuccess=false`, so the service keeps cached values and getters return inline defaults.

**RemoteConfigFetchResult**:
DTO: `Parameters` (Dict), `Experiments` (List of `AbExperimentInfo`), `IsSuccess` (bool).

**InMemoryPlayerPrefs**:
In-memory `IPlayerPrefs` implementation. Used by Firebase backend for FID storage and by tests. No persistence — `Save()` is a no-op.

**Caching**:
Stored in PlayerPrefs under key `rc_cache` as a JSON blob. Loaded on construction. Written on every successful `FetchAsync()`. Corrupted JSON is caught and discarded.

**FID lifecycle**:
The backend registers a Firebase Installation ID (FID) on first use. Persisted in PlayerPrefs (`rc_fid_data`). No auth tokens — `appInstanceId` alone suffices for Remote Config fetch.

## Relationships

- **RemoteConfig → Core**: Uses `IPlayerPrefs`, `IExperimentInfoProvider`, `AbExperimentInfo`.
- **RemoteConfig → Firebase**: Production backend (`FirebaseRemoteConfigBackend`) in `com.longleap.firebase`.
- **RemoteConfig → Analytics**: `RemoteConfigService` implements `IExperimentInfoProvider` for A/B experiment auto-tagging.
