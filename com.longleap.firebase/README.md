# LongLeap Firebase Backend

Production Firebase backends for LongLeap services: anonymous auth, cloud saves, leaderboards and Remote Config. Pure REST over `UnityWebRequest`, no Firebase Unity SDK, so it works on WebGL. Every class takes an optional HTTP handler, so tests can run without a network.

---

## Overview

| Feature | Provider interface | Firebase implementation | Firebase product |
|---|---|---|---|
| Auth | `IAuthProvider` | `FirebaseAnonymousAuthProvider` | Authentication (anonymous) |
| Cloud Saves | `ICloudSaveProvider`, `IAccountCloudSaveScope` | `FirebaseCloudSaveProvider` | Realtime Database |
| Leaderboards | `ILeaderboardProvider` | `FirebaseLeaderboardProvider` | Realtime Database |
| Remote Config | `IRemoteConfigProvider` | `FirebaseRemoteConfigProvider` | Remote Config + Installations |

Interfaces come from `com.longleap.core`, except `IRemoteConfigProvider` (from `com.longleap.remoteconfig`) and `ILeaderboardProvider` (from `com.longleap.xsollaid`).

`FirebaseLeaderboardProvider` is in its own assembly, `LongLeap.Firebase.Leaderboards` (`Runtime.Leaderboards/`), compiled only with `XSOLLA_ID`, because the leaderboard contracts exist only with that define. This package does not declare a dependency on `com.longleap.xsollaid`; that assembly needs it present.

## Setup

### 1. Firebase project

In the Firebase console:

- **Authentication → Sign-in method**: enable **Anonymous**.
- **Realtime Database**: create a database and apply the rules below.
- **Remote Config** (optional): publish your parameters.

### 2. Config asset

Create **Assets → Create → LongLeap Services → Firebase Config** (`FirebaseConfigSO`). By convention it lives at `Resources/Backend/FirebaseConfig.asset`. The package does not load it itself; game code passes it in (see [Initialization](#initialization)).

| Field | Used by | Where to find it |
|---|---|---|
| `ApiKey` | Auth, Remote Config | Project settings → General → Web API key |
| `DatabaseUrl` | Cloud saves, leaderboards | Realtime Database → Data tab, e.g. `https://<project>-default-rtdb.firebaseio.com` |
| `GoogleAppId` | Remote Config (required) | Project settings → Your apps → App ID |
| `ProjectNumber` | Remote Config (required) | Project settings → General → Project number (not the project ID) |

These values are compiled into the client and are not secrets. Access control is the job of the database rules.

### 3. Realtime Database rules

The leaderboard queries order by `negScore`. **Without the `.indexOn` rule, every leaderboard read fails** with "Index not defined". Starting point:

```json
{
  "rules": {
    "cloud_saves": {
      "$uid": {
        ".read": "auth != null && auth.uid === $uid",
        ".write": "auth != null && auth.uid === $uid"
      }
    },
    "xsolla_saves": {
      "$accountId": {
        ".read": "auth != null",
        ".write": "auth != null"
      }
    },
    "leaderboards": {
      "$boardId": {
        ".read": "auth != null",
        ".indexOn": ["negScore"],
        "$uid": {
          ".write": "auth != null && auth.uid === $uid",
          ".validate": "newData.hasChildren(['score', 'negScore', 'displayName', 'timestamp'])"
        }
      }
    }
  }
}
```

See [Limitations](#limitations) for what these rules can and cannot protect.

## Initialization

```csharp
var config = Resources.Load<FirebaseConfigSO>("Backend/FirebaseConfig");

var auth = new FirebaseAnonymousAuthProvider(config.ApiKey);
var http = new FirebaseHttpClient(config.DatabaseUrl, auth);

var cloudSaveProvider   = new FirebaseCloudSaveProvider(http, auth, playerPrefs); // playerPrefs: IPlayerPrefs, optional
var leaderboardProvider = new FirebaseLeaderboardProvider(http);
var remoteConfigProvider = new FirebaseRemoteConfigProvider(config, playerPrefs);
```

`auth` and `http` are meant to be shared: create one of each and pass them to every provider.

### Account saves

`FirebaseCloudSaveProvider` also implements `IAccountCloudSaveScope`. By default it reads and writes the guest branch of the anonymous Firebase user. An account flow (for example `com.longleap.xsollaid`) switches it:

```csharp
IAccountCloudSaveScope scope = cloudSaveProvider;

var preview = await scope.PeekAccountAsync(accountId); // read without switching
scope.UseAccount(accountId); // all operations now go to xsolla_saves/{accountId}
scope.UseGuest();            // back to cloud_saves/{uid}
```

The selected scope is saved to `IPlayerPrefs` (key `longleap_cloudsave_scope`) and restored in the constructor, so it survives restarts. Without `IPlayerPrefs` the provider always starts in guest scope.

### Test handlers

Each class has a constructor overload that takes the HTTP call as a delegate: `(method, url, body) => responseBody`.

```csharp
Func<string, string, string, Task<string>> fakeHttp =
    (method, url, body) => Task.FromResult("{\"mock\": true}");

var auth = new FirebaseAnonymousAuthProvider("test-key", fakeHttp);
var http = new FirebaseHttpClient("https://test.firebaseio.com", auth, fakeHttp);
var rc   = new FirebaseRemoteConfigProvider(config, playerPrefs, fakeHttp);
```

The delegate type is the same, but the failure contract is not:

| Class | Handler signals failure by |
|---|---|
| `FirebaseAnonymousAuthProvider` | returning `null` |
| `FirebaseRemoteConfigProvider` | returning `null` |
| `FirebaseHttpClient` | throwing (`GetAsync`/`PatchAsync` catch it and return `null`; `PutAsync`/`DeleteAsync` let it propagate) |

A test handler does not receive a cancellation token. `FirebaseRemoteConfigProvider` still honours cancellation around it: an answer that arrives after cancellation is dropped and `OperationCanceledException` is thrown.

`FirebaseAnonymousAuthProvider` always reads and writes the real `UnityEngine.PlayerPrefs` key `longleap_auth`, even with a test handler.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `FirebaseConfigSO` | ScriptableObject | Project settings: `ApiKey`, `DatabaseUrl`, `GoogleAppId`, `ProjectNumber` |
| `FirebaseAnonymousAuthProvider` | Sealed class `: IAuthProvider` | `(string apiKey)` or `(string apiKey, handler)`. Cached token → refresh → anonymous sign-up. Calls are serialised with a `SemaphoreSlim` |
| `FirebaseHttpClient` | Sealed class | `(string databaseUrl, IAuthProvider)` or `(..., handler)`. `GetAsync`, `PatchAsync`, `PutAsync`, `DeleteAsync`. Attaches a fresh ID token to every request; returns `null` when no token is available |
| `FirebaseCloudSaveProvider` | Sealed class `: ICloudSaveProvider, IAccountCloudSaveScope` | `(FirebaseHttpClient, IAuthProvider, IPlayerPrefs = null)`. Guest saves at `cloud_saves/{uid}/{key}`, account saves at `xsolla_saves/{accountId}/{key}`. The uid is resolved on first use |
| `FirebaseLeaderboardProvider` | Sealed class `: ILeaderboardProvider` (`IDisposable`) | `(FirebaseHttpClient)`. Entries at `leaderboards/{boardId}/{uid}`. Writes a submit only if it beats the stored score. After `Dispose()` every call returns an empty or failed result |
| `FirebaseRemoteConfigProvider` | Sealed class `: IRemoteConfigProvider` | `(FirebaseConfigSO, IPlayerPrefs)` or `(..., handler)`. Registers an FID, then fetches parameters and A/B experiment assignments. `FetchAsync(CancellationToken)` aborts the request on cancellation (the token comes from `RemoteConfigService`, cancelled on `Dispose`). Throws `ArgumentException` if `GoogleAppId` is empty |

## Data layout

| Path | Written by | Contents |
|---|---|---|
| `cloud_saves/{uid}/{key}` | `FirebaseCloudSaveProvider` (guest scope) | JSON string (a `CloudSaveEnvelope` produced by `com.longleap.core`) |
| `xsolla_saves/{accountId}/{key}` | `FirebaseCloudSaveProvider` (account scope) | Same as above |
| `leaderboards/{boardId}/{uid}` | `FirebaseLeaderboardProvider` | `{ score, negScore, displayName, timestamp }` |

Local keys:

| Key | Storage | Owner |
|---|---|---|
| `longleap_auth` | `UnityEngine.PlayerPrefs` | Auth: `{idToken, refreshToken, uid, expiresAt}` |
| `longleap_cloudsave_scope` | `IPlayerPrefs` | Cloud saves: selected account id. Shared on purpose with `XsollaCloudSaveScope` |
| `rc_fid_data` | `IPlayerPrefs` | Remote Config: Firebase Installation ID |

## Internals

### Token lifecycle

Cached in `PlayerPrefs` under `longleap_auth`. `LoginAsync` returns the cached token if it is more than 5 minutes from expiry. Otherwise it refreshes the token through `securetoken.googleapis.com`. If the refresh fails, it signs up a **new** anonymous user, which gets a new uid, so the previous guest save is no longer reachable.

### HTTP client

`FirebaseHttpClient` is shared by the cloud save and leaderboard providers. It builds `{databaseUrl}/{path}.json?auth={idToken}{query}` and calls `IAuthProvider.LoginAsync()` before every request. The default transport has a 10-second timeout and masks the token (`auth=***`) in its exception messages.

### Leaderboard negScore

RTDB queries sort ascending only. Each entry stores `negScore`: `-score` for `Descending` boards, `score` for `Ascending` ones. All queries use `orderBy="negScore"`.

### Leaderboard submits

`SubmitScoreAsync` reads the player's stored score first. It writes only if the new score is better (`>` for `Descending`, `<` for `Ascending`). The write is one multi-location `PATCH` with a server timestamp. It returns `false` both when the score is not better and when the request fails.

### Remote Config FID

On first fetch, the provider registers a Firebase Installation ID via `firebaseinstallations.googleapis.com` and stores the ID the server returns in `rc_fid_data`. Later fetches reuse it as `appInstanceId`. This class does not cache parameter values; `com.longleap.remoteconfig` does that.

Parameter values are passed on exactly as the server sends them; date-like strings are not reformatted. A response without `entries` (state `EMPTY_CONFIG` or `NO_TEMPLATE`) is a success with no parameters. A `NO_CHANGE` response is reported as a failure, so the cached values stay.

## Limitations

- **Account saves are protected only by an anonymous token.** Account-scope requests are signed with the anonymous Firebase token, so database rules cannot tell which account the caller owns. With the rules above, any signed-in client that knows an account id can read and write that account's save.
- **Leaderboard integrity is client-side.** The rules can make a player write only under their own uid, but the "only better scores" check and the score value itself are not verified by the server.
- **Failures look like empty data.** `GetStringAsync`, `HasKeyAsync` and `LoadAllAsync` return `null`, `false` or an empty dictionary both when there is nothing stored and when the request failed.
- **Rank queries scale with board size.** `GetPlayerRankAsync` downloads every entry ranked above the player plus a shallow list of all keys.
