# LongLeap Firebase Backend

Production Firebase backend implementations — pure REST, no Firebase Unity SDK. Swappable via injectable HTTP handlers.

---

## Overview

| Feature | Provider interface | Firebase implementation |
|---|---|---|
| Auth | `IAuthProvider` | `FirebaseAnonymousAuthProvider` |
| Cloud Saves | `ICloudSaveProvider` | `FirebaseCloudSaveProvider` |
| Leaderboards | `ILeaderboardProvider` | `FirebaseLeaderboardProvider` |
| Remote Config | `IRemoteConfigProvider` | `FirebaseRemoteConfigBackend` |

## Data Sources

**FirebaseConfigSO** — `[CreateAssetMenu(menuName = "LongLeap Services/Firebase Config")]`, expected at `Resources/Backend/FirebaseConfig.asset`:

| Field | Purpose |
|---|---|
| `ApiKey` | Firebase project API key |
| `DatabaseUrl` | Realtime Database URL |
| `GoogleAppId` | Google App ID (Remote Config) |
| `ProjectNumber` | Firebase project number (Remote Config) |

## Public API

### Configuration

| Type | Kind | Purpose |
|---|---|---|
| `FirebaseConfigSO` | ScriptableObject | Firebase project credentials |

### HTTP Client

| Type | Kind | Purpose |
|---|---|---|
| `FirebaseHttpClient` | Sealed class | Constructor: `(string databaseUrl, IAuthProvider)`. Second overload with injectable handler. Methods: `GetAsync`, `PatchAsync`, `PutAsync`, `DeleteAsync`. Auto-injects auth token |

### Auth

| Type | Kind | Purpose |
|---|---|---|
| `FirebaseAnonymousAuthProvider` | Sealed class `: IAuthProvider` | Constructor: `(string apiKey)`. Second overload with injectable handler. Flow: cached token -> refresh -> sign-up. SemaphoreSlim-guarded |

### Cloud Saves

| Type | Kind | Purpose |
|---|---|---|
| `FirebaseCloudSaveProvider` | Sealed class `: ICloudSaveProvider` | Constructor: `(FirebaseHttpClient, IAuthProvider)`. Stores at `cloud_saves/{uid}/{key}.json`. UID resolved lazily |

### Leaderboards

| Type | Kind | Purpose |
|---|---|---|
| `FirebaseLeaderboardProvider` | Sealed class `: ILeaderboardProvider, IDisposable` | Constructor: `(FirebaseHttpClient)`. Stores at `leaderboards/{boardId}/{uid}/`. Only-better submits. `Dispose()` sets flag |

### Remote Config

| Type | Kind | Purpose |
|---|---|---|
| `FirebaseRemoteConfigBackend` | Sealed class `: IRemoteConfigProvider` | Constructor: `(FirebaseConfigSO, IPlayerPrefs)`. Second overload with injectable handler. FID registration -> REST fetch |

## Initialization

```csharp
var config = Resources.Load<FirebaseConfigSO>("Backend/FirebaseConfig");
var auth = new FirebaseAnonymousAuthProvider(config.ApiKey);
var http = new FirebaseHttpClient(config.DatabaseUrl, auth);

var cloudSaveProvider = new FirebaseCloudSaveProvider(http, auth);
var leaderboardProvider = new FirebaseLeaderboardProvider(http);
```

### With test handlers (all HTTP providers support injectable handlers)

```csharp
var mockHttp = (string m, string u, string b) => Task.FromResult("{\"mock\": true}");
var auth = new FirebaseAnonymousAuthProvider("test-key", mockHttp);
var http = new FirebaseHttpClient("https://test.firebaseio.com", auth, mockHttp);
```

## Internals

### Token lifecycle

Cached in `PlayerPrefs` key `longleap_auth` as JSON `{idToken, refreshToken, uid, expiresAt}`. Flow: cache hit -> refresh if within 5min of expiry -> anonymous sign-up. All operations serialised via `SemaphoreSlim`.

### HTTP client architecture

`FirebaseHttpClient` is shared by CloudSaveProvider and LeaderboardProvider. Constructs RTDB URLs (`{databaseUrl}/{path}.json?auth={token}`). Resolves auth token per-request. Sanitizes token from error logs.

### Leaderboard negScore

Firebase RTDB only supports ascending `orderBy`. Each entry stores `negScore`: `-score` for Descending, `score` for Ascending. Queries use `orderBy="negScore"`.

### Leaderboard only-better submits

`SubmitScoreAsync` reads existing score first. New score only submitted if it beats existing (> for Descending, < for Ascending). Written as multi-location `PATCH` with server timestamp.

### Remote Config FID

Registers Firebase Installation ID on first use via REST API, cached in `PlayerPrefs` key `rc_fid_data`. Reused for subsequent fetches.