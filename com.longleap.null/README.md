# LongLeap Null

Null-object backends for every service-provider seam. Each method returns a valid, successful result — zero side effects, zero network I/O, zero runtime state. Always explicitly opted into at construction time.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `NullAuthProvider` | Class (sealed) | Implements `IAuthProvider`. `LoginAsync()` always returns `AuthResult.Success` with `IdToken.None` and `UserId("anonymous")`. |
| `NullCloudSaveProvider` | Class (sealed) | Implements `ICloudSaveProvider`. All reads return null/false/empty; all writes are no-ops. |
| `NullLeaderboardProvider` | Class (sealed) | Implements `ILeaderboardProvider`, `IDisposable`. Submissions pretend to succeed; queries return empty `LeaderboardPage`. `Dispose()` is a no-op. |

## Usage

The key invariant: **null backends are never a silent fallback**. The game checks a condition and chooses explicitly:

```csharp
bool useFirebase = firebaseConfig != null;

IAuthProvider auth = useFirebase
    ? new FirebaseAnonymousAuthProvider(firebaseConfig.ApiKey)
    : new NullAuthProvider();

FirebaseHttpClient http = useFirebase
    ? new FirebaseHttpClient(firebaseConfig.DatabaseUrl, auth)
    : null;

ICloudSaveProvider cloudSave = useFirebase
    ? new FirebaseCloudSaveProvider(http, auth, playerPrefs)
    : new NullCloudSaveProvider();

ILeaderboardProvider leaderboard = useFirebase
    ? new FirebaseLeaderboardProvider(http)
    : new NullLeaderboardProvider();
```

All three are standalone — use any mix independently. A game might have real auth but null cloud saves, or vice versa.

## Internals

### Design principle

Every method returns synchronously or with an already-completed `Task`. No `await` chains, no coroutines, no callbacks. The calling service sees a valid result and continues without special-casing.

### No dependencies beyond Core

The only dependency is `LongLeap.Core` for the provider interfaces (`IAuthProvider`, `ICloudSaveProvider`, `ILeaderboardProvider`) and domain types (`AuthResult`, `UserId`, `IdToken`, `LeaderboardPage`, `PlayerRank`). No Unity APIs, no platform-specific code — safe for WebGL, Editor, and standalone builds.

### `NullLeaderboardProvider` and `IDisposable`

`Dispose()` is a no-op. The interface requires it (the Firebase backend holds resources), but the null backend has none to release.