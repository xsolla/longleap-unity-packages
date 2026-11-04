# LongLeap Core

Foundation package — defines all core abstractions, interfaces, domain types, and the concrete service classes for cloud saves and leaderboards. Other LongLeap packages provide the concrete backend implementations.

---

## Overview

| Module | Defines | Implemented elsewhere |
|---|---|---|
| Contracts | `IPlayerPrefs`, `IPersistentStorage`, `IExperimentInfoProvider`, `AbExperimentInfo` | Unity implementations of `IPlayerPrefs` / `IPersistentStorage` ship here (see [Storage](#storage)); `IExperimentInfoProvider` by `RemoteConfigService` |
| Auth | `IAuthProvider`, `AuthResult`, `UserId`, `IdToken` | `NullAuthProvider`, `FirebaseAnonymousAuthProvider` |
| Cloud Saves | `ICloudSaveService`/`ICloudSaveProvider`, `CloudSaveKey`, `CloudSaveService`, extensions | `NullCloudSaveProvider`, `FirebaseCloudSaveProvider` |
| Leaderboards | `ILeaderboardService`/`ILeaderboardProvider`/`ILeaderboard`, `LeaderboardService`, value types | `NullLeaderboardProvider`, `FirebaseLeaderboardProvider` |

## Public API

### Auth

| Type | Kind | Purpose |
|---|---|---|
| `IAuthProvider` | Interface | `Task<AuthResult> LoginAsync()` |
| `AuthResult` | Abstract class | Discriminated union: `Success(Token, Uid)` or `Failure(Error)`. Private constructor |
| `UserId` | Readonly struct `: IEquatable<UserId>` | Typed wrapper. `Value`, `IsValid`, `None` sentinel, value equality |
| `IdToken` | Readonly struct `: IEquatable<IdToken>` | Typed wrapper around JWT. `Value`, `IsValid`, `None` sentinel |

### Cloud Saves

| Type | Kind | Purpose |
|---|---|---|
| `ICloudSaveService` | Interface, `IDisposable` | `InitializeAsync()`, CRUD with `CloudSaveKey` |
| `ICloudSaveProvider` | Interface | Provider seam. `LoadAllAsync()`, single-key CRUD |
| `CloudSaveKey` | Readonly struct, `[Serializable]` | Typed newtype. Validates against forbidden chars (`. $ # [ ] /`) |
| `CloudSaveService` | Sealed class | Constructor: `(ICloudSaveProvider)`. Offline-first: local cache, dirty tracking, timestamp-based conflict resolution, semaphore-guarded |
| `CloudSaveProviderExtensions` | Static class | `SetIntAsync`/`GetIntAsync`, `SetFloatAsync`/`GetFloatAsync` on `ICloudSaveProvider` |

### Leaderboards

| Type | Kind | Purpose |
|---|---|---|
| `ILeaderboardService` | Interface | `GetBoard(BoardId, SortDirection)`, `FlushAsync()`, `Dispose()`, event `OnTopRankAchieved(BoardId, int)` |
| `ILeaderboardProvider` | Interface | Provider seam with score submission, top-N, rank, surroundings |
| `ILeaderboard` | Interface | Per-board handle: `Id`, `SubmitScoreAsync(long)`, `GetTopAsync(int)`, `GetMyRankAsync()`, `GetSurroundingsAsync(int)` |
| `LeaderboardService` | Sealed class, `IDisposable` | Constructor: `(string prefix, ILeaderboardProvider, IAuthProvider)`. Batching, prefixing, caching, auth fallback, top-rank event |
| `BoardId` | Readonly struct | Typed newtype: `^[a-zA-Z][a-zA-Z0-9_-]{0,39}$`. `None` sentinel, `IsNone`, `IsValid` |
| `BoardDefinition` | Readonly struct | `Id` (BoardId) + `Sort` (SortDirection) |
| `SortDirection` | Enum | `Descending`, `Ascending` |
| `SubmitResult` | Readonly struct | `IsAccepted`, `IsNewBest`, `Score` |
| `PlayerRank` | Readonly struct | `Rank` (int?), `Score` (long?), `TotalPlayers` (int) |
| `LeaderboardEntry` | Readonly struct | `Uid`, `DisplayName`, `Score`, `Rank` (1-based), `Timestamp` |
| `LeaderboardPage` | Sealed class | `Entries`, `Cursor` (object?), `HasMore`. Static `Empty()` factory |

### Contracts

| Type | Kind | Purpose |
|---|---|---|
| `IPlayerPrefs` | Interface | `GetString`/`GetInt`/`GetFloat`(key, default), `Set*`(key, val), `HasKey`, `DeleteKey`, `Save` |
| `IPersistentStorage` | Interface | Async key-value string storage: `LoadAsync`, `SaveAsync`, `HasKeyAsync`, `DeleteAsync` |
| `IExperimentInfoProvider` | Interface | `IReadOnlyList<AbExperimentInfo> ActiveExperiments` |
| `AbExperimentInfo` | Readonly struct | `ExperimentId` (string), `VariantIndex` (int) |

### Storage

Unity implementations of the storage contracts.

| Type | Kind | Purpose |
|---|---|---|
| `UnityPlayerPrefsAdapter` | Sealed class `: IPlayerPrefs` | Pass-through to `UnityEngine.PlayerPrefs` |
| `PlayerPrefsPersistentStorage` | Sealed class `: IPersistentStorage` | Key-value storage in `PlayerPrefs`. Synchronous, saves on every write. Works on WebGL |
| `FilePersistentStorage` | Sealed class `: IPersistentStorage` | One `<key>.json` file per key in a given folder. Runs on the thread pool, so not for WebGL |

## Initialization

Each service is constructed independently:

```csharp
var saves = new CloudSaveService(cloudSaveProvider);
await saves.InitializeAsync();

var leaderboards = new LeaderboardService("v1_", leaderboardProvider, authProvider);
```

## Usage

### Auth — discriminated union

```csharp
var result = await authProvider.LoginAsync();
if (result is AuthResult.Success s) { /* s.Token, s.Uid */ }
else if (result is AuthResult.Failure f) { /* f.Error */ }
```

### Cloud Saves

```csharp
var key = new CloudSaveKey("progress");
await saves.SetStringAsync(key, "42");
var value = await saves.GetStringAsync(key);
```

### Storage

```csharp
IPlayerPrefs prefs = new UnityPlayerPrefsAdapter();
prefs.SetString("last_level", "forest");
string level = prefs.GetString("last_level", "default_level");

// WebGL-safe; completes synchronously, call from the main thread.
IPersistentStorage storage = new PlayerPrefsPersistentStorage();
await storage.SaveAsync("settings", json);
string data = await storage.LoadAsync("settings"); // null if missing

// Desktop / mobile. The folder is created on the first save.
IPersistentStorage files = new FilePersistentStorage(Application.persistentDataPath + "/saves");
await files.SaveAsync("profile", json);
```

`FilePersistentStorage` writes `<key>.json.tmp`, deletes the old file and moves the temp file into place. A failed save is logged as an error and not rethrown; a file that cannot be read loads as `null`. Keys are used as file names unchanged, so keep them to characters that are valid in a file name.

### Leaderboards

```csharp
var board = leaderboards.GetBoard(new BoardId("score"), SortDirection.Descending);
await board.SubmitScoreAsync(1500);
await leaderboards.FlushAsync();
var top10 = await board.GetTopAsync(10);
```

## Internals

### Cloud Saves — conflict resolution

On `InitializeAsync`, compares local and cloud Unix timestamps. Cloud newer -> overwrites local. Local newer -> marks dirty for push. Last-write-wins. Local cache uses `PlayerPrefs` keys: `cloudsave_{key}`, `cloudsave_ts_{key}`, `cloudsave_dirty`.

### Leaderboards — batching

`SubmitScoreAsync` stores best pending score per board. Only the best is kept. `FlushAsync` sends all queued scores in one batch. Uses `SystemInfo.deviceUniqueIdentifier` as UID fallback.

### Leaderboards — prefixing

All board IDs are prefixed at construction (e.g. `"v1_"`) to isolate per-game on shared backends.

### Leaderboards — top-rank event

`OnTopRankAchieved` fires during `FlushAsync` when player rank is <= 10. Only fires once per session per board.