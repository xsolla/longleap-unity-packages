# LongLeap Core

Foundation package for LongLeap. It holds:

- the **contracts** other packages implement (auth, cloud saves, leaderboards, leagues, ads, storage, A/B experiments);
- the **backend-agnostic services** built on those contracts (`CloudSaveService`, `LeaderboardService`, `LeagueService`);
- small **shared types** (`RewardKey`, `ResetSchedule`) and the Unity implementations of the storage contracts;
- an **Editor framework** for project options, presets, build targets and warnings, used by the Build Manager window.

Backends live in other packages: `com.longleap.firebase`, `com.longleap.xsollaid`, `com.longleap.gamepix`, and null backends in `com.longleap.nullproviders`. Terms are defined in [CONTEXT.md](CONTEXT.md).

Requires Unity 6000.0+ and `com.unity.nuget.newtonsoft-json`.

---

## Contents

| Module | Namespace | Defines | Implemented elsewhere |
|---|---|---|---|
| Storage | `LongLeap.Core` | `IPlayerPrefs`, `IPersistentStorage` + 4 Unity implementations | — |
| Experiments | `LongLeap.Core` | `IExperimentInfoProvider`, `AbExperimentInfo` | `RemoteConfigService` (remoteconfig), read by analytics |
| Shared types | `LongLeap.Core` | `RewardKey`, `ResetSchedule` | used by daily rewards and daily quests |
| Auth | `LongLeap.Core.Auth` | `IAuthProvider`, `AuthResult`, `UserId`, `IdToken` | null, Firebase, Xsolla ID |
| Cloud saves | `LongLeap.Core.CloudSaves` | `ICloudSaveService`, `ICloudSaveProvider`, `CloudSaveService`, scope/batch/bulk interfaces | null, Firebase, Xsolla ID |
| Leaderboards | `LongLeap.Core.Leaderboards` | `ILeaderboardService`, `ILeaderboardProvider`, `LeaderboardService`, value types | null, Firebase, Xsolla ID |
| Leagues | `LongLeap.Core.Leagues` | `ILeagueService`, `ILeagueProvider`, `LeagueService`, `LeagueState` model | null, Xsolla ID |
| Ads / portal | `LongLeap.Core.Ads` | `IAdsProvider`, `AdResult`, `AdsDebug`, `IPortalLocale`, `NoPortalLocale` | null, GamePix |
| Editor | `LongLeap.Core.Editor` | project options, presets, build targets, warnings | Build Manager, GamePix, DevTools, BackPack |
| Polyfill | `System.Runtime.CompilerServices` | `IsExternalInit` (C# 9 records / `init`) | — |

---

## Storage

| Type | Kind | Purpose |
|---|---|---|
| `IPlayerPrefs` | Interface | `Get/Set String/Int/Float`, `HasKey`, `DeleteKey`, `Save` |
| `IPersistentStorage` | Interface | Async string key-value storage: `LoadAsync` (null if missing), `SaveAsync`, `HasKeyAsync`, `DeleteAsync` |
| `UnityPlayerPrefsAdapter` | `IPlayerPrefs` | Pass-through to `UnityEngine.PlayerPrefs`. Written to disk on `Save()` or normal quit |
| `InMemoryPlayerPrefs` | `IPlayerPrefs` | Dictionary-backed, nothing persists. For tests and sessions that must not write to disk |
| `PlayerPrefsPersistentStorage` | `IPersistentStorage` | Stores in `PlayerPrefs`, completes synchronously, calls `PlayerPrefs.Save()` on every write. Works on WebGL; call from the main thread |
| `FilePersistentStorage` | `IPersistentStorage` | One `<key>.json` per key in a folder. Runs on the thread pool — **not for WebGL** |

```csharp
IPlayerPrefs prefs = new UnityPlayerPrefsAdapter();
prefs.SetString("last_level", "forest");

// WebGL-safe.
IPersistentStorage storage = new PlayerPrefsPersistentStorage();
await storage.SaveAsync("settings", json);
string data = await storage.LoadAsync("settings"); // null if missing

// Desktop / mobile. The folder is created on the first save.
IPersistentStorage files = new FilePersistentStorage(Application.persistentDataPath + "/saves");
await files.SaveAsync("profile", json);
```

`FilePersistentStorage` writes `<key>.json.tmp`, deletes the old file and moves the temp file into place. A failed save is logged and not rethrown; an unreadable file loads as `null`. Keys are used as file names unchanged, so keep them to valid file-name characters.

## Shared types

- **`RewardKey`** — a string newtype naming what to grant. Packages never interpret it; the game decides what `"soft"` means. `None` sentinel, ordinal equality; empty string throws.
- **`ResetSchedule(resetHourUtc)`** — daily period math for a UTC reset hour: `PeriodStart`, `NextReset`, `TimeUntilReset`. Hours outside 0..23 wrap (25 → 1, −1 → 23).

## Auth

| Type | Kind | Purpose |
|---|---|---|
| `IAuthProvider` | Interface | `Task<AuthResult> LoginAsync()` |
| `AuthResult` | Abstract class | Either `Success(Token, Uid)` or `Failure(Error)`; no other subclasses are possible |
| `UserId` | Readonly struct | Typed uid. `Value`, `IsValid`, `None`; empty string throws |
| `IdToken` | Readonly struct | Typed JWT. `ToString()` never prints the token |

```csharp
var result = await authProvider.LoginAsync();
if (result is AuthResult.Success s) { /* s.Token, s.Uid */ }
else if (result is AuthResult.Failure f) { /* f.Error */ }
```

## Cloud saves

| Type | Kind | Purpose |
|---|---|---|
| `ICloudSaveService` | Interface, `IDisposable` | `InitializeAsync`, `Set/GetStringAsync`, `HasKeyAsync`, `DeleteKeyAsync`, `DeleteAllAsync` |
| `CloudSaveService` | Sealed class | `new CloudSaveService(ICloudSaveProvider, IPlayerPrefs)`. Also: `SnapshotAsync`, `FlushPendingAsync`, `ResetLocalCacheAsync` |
| `ICloudSaveProvider` | Interface | Backend seam: per-key CRUD, `LoadAllAsync`, `DeleteAllAsync`. Values arrive as envelopes |
| `ICloudSaveBatchProvider` | Optional interface | `SetManyAsync(entries, deletes)` — if the provider implements it, writes are batched |
| `IAccountCloudSaveScope` | Optional interface | Guest vs account save: `UseGuest`, `UseAccount`, `PeekAccountAsync` (read without switching) |
| `ICloudSaveBulkAccess` | Interface | `SnapshotAsync()` — the whole save in one read. Implemented by `CloudSaveService` |
| `CloudSaveKey` | Readonly struct | Key newtype. Throws on `. $ # [ ] /` |
| `CloudSaveEntry` | Readonly struct | `Value` + `TimestampUnixSeconds` (0 = unknown) |
| `CloudSaveEnvelope` | Static class | The `{"v": value, "ts": seconds}` cloud format. `Parse` never throws; non-envelope input is returned as-is with ts 0 |
| `CloudSaveProviderExtensions` | Static class | `Set/GetIntAsync`, `Set/GetFloatAsync` on `ICloudSaveProvider` |

```csharp
var saves = new CloudSaveService(cloudSaveProvider, new UnityPlayerPrefsAdapter());
await saves.InitializeAsync();

var key = new CloudSaveKey("progress");
await saves.SetStringAsync(key, "42");
string value = await saves.GetStringAsync(key);
```

### How it works

- **Local-first.** Every write goes to memory and `PlayerPrefs` first and is readable at once; the cloud push follows. Reads never wait for the network.
- **Local keys.** `cloudsave_{key}` (value), `cloudsave_ts_{key}` (timestamp), `cloudsave_dirty` (keys not yet pushed), `cloudsave_index` (every key with a local copy).
- **Merge on `InitializeAsync`.** Per key, the newer Unix timestamp wins (last write wins): a newer cloud value overwrites the local one; a newer local value is marked dirty and pushed.
- **Before `InitializeAsync`.** The service can already be read, written and snapshotted (used by a save switch to show and apply the player's choice before the cloud merge).
- **Pushing.** Without a batch provider, each write awaits a per-key push. With `ICloudSaveBatchProvider`, writes return immediately and dirty keys go out together after a 500 ms pause, split into chunks of up to ~3M characters. A failed push leaves keys dirty for the next attempt. `FlushPendingAsync` pushes now and waits.
- **Identity switch.** `ResetLocalCacheAsync` forgets the local mirror and leaves the cloud untouched; the service then counts as not initialized. `DeleteAllAsync` erases the owner's save in the cloud too.

## Leaderboards

| Type | Kind | Purpose |
|---|---|---|
| `ILeaderboardService` | Interface | `GetBoard(BoardId, SortDirection)`, `FlushAsync`, `GetPlayerUidAsync`, `GetPlayerDisplayName`, event `OnTopRankAchieved(BoardId, rank)` |
| `LeaderboardService` | Sealed class | `new LeaderboardService(prefix, ILeaderboardProvider, IAuthProvider, Func<string> displayName = null)` |
| `ILeaderboard` | Interface | Per-board handle: `SubmitScoreAsync`, `GetTopAsync`, `GetMyRankAsync`, `GetSurroundingsAsync` |
| `ILeaderboardProvider` | Interface | Backend seam; receives the prefixed board id and the uid |
| `BoardId` | Readonly struct | `^[a-zA-Z][a-zA-Z0-9_-]{0,39}$`. An invalid id logs an error and becomes `"invalid_board_id"` (`IsValid == false`) |
| `BoardDefinition` | Readonly struct | `Id` + `Sort` |
| `SortDirection` | Enum | `Descending`, `Ascending` |
| `SubmitResult` | Readonly struct | `IsAccepted`, `IsNewBest`, `Score` |
| `PlayerRank` | Readonly struct | `Rank` (int?), `Score` (long?), `TotalPlayers` |
| `LeaderboardEntry` | Readonly struct | `Uid`, `DisplayName`, `Score`, `Rank` (1-based), `Timestamp` |
| `LeaderboardPage` | Sealed class | `Entries`, `Cursor`, `HasMore`, `Empty()` |

```csharp
var leaderboards = new LeaderboardService("dev_", leaderboardProvider, authProvider, () => playerName);
var board = leaderboards.GetBoard(new BoardId("score"), SortDirection.Descending);
await board.SubmitScoreAsync(1500);   // queued
await leaderboards.FlushAsync();      // sent
var top10 = await board.GetTopAsync(10);
```

### How it works

- **Batching.** `SubmitScoreAsync` only queues the score, keeping the best pending one per board (higher for `Descending`, lower for `Ascending`). `FlushAsync` sends the queue; failed boards are re-queued. `SubmitResult.IsAccepted` means "queued", not "accepted by the server".
- **Prefix.** Every board id sent to the provider is `prefix + id` (e.g. `"dev_"` for development builds). Keep prefix + id within 40 characters.
- **Uid.** Taken from `IAuthProvider.LoginAsync`; if auth fails, a GUID stored in `PlayerPrefs` under `longleap_device_id` is used. UI must compare row uids with `GetPlayerUidAsync()`.
- **Display name.** From the resolver passed to the constructor; `"Player"` if none or empty.
- **Caching.** Top and surroundings pages are cached per board for the lifetime of the service and dropped after the player's own accepted submit. On a failed request the cached page (or an empty one) is returned.
- **Top-rank event.** After each accepted submit in `FlushAsync`, `OnTopRankAchieved` fires if the player's rank is 10 or better.

## Leagues

A weekly league for the current player: submit scores, read the state, receive rewards for closed periods.

| Type | Kind | Purpose |
|---|---|---|
| `ILeagueService` | Interface | `LeagueId`, `State`, `RefreshAsync`, `SubmitScoreAsync`, events `StateChanged`, `RewardClaimed`; debug: `DebugFillGroupAsync`, `DebugClosePeriodAsync` |
| `LeagueService` | Sealed class | `new LeagueService(prefix, leagueId, ILeagueProvider, Func<string> displayName = null)` |
| `ILeagueProvider` | Interface | Backend seam; every method throws on a failed request |
| `LeagueState` | Sealed class | `Tiers`, `Tier`, `Period`, `Group` (null until the player submits this period), `LastResult`, `Unclaimed` |
| `LeaguePeriod`, `LeagueGroup`, `LeagueEntry`, `LeagueResult`, `LeagueClaim` | Model | Period timing, group standings and zones, closed-period results, claim reply |

```csharp
var league = new LeagueService("dev_", "weekly", leagueProvider, () => playerName);
league.RewardClaimed += result => wallet.Add(result.Reward); // credit here, exactly once
await league.RefreshAsync();
await league.SubmitScoreAsync(runScore);
```

- A failed submit is kept and added to the next submit (within the same launch).
- Whenever a state arrives with unclaimed results, the service claims them. `RewardClaimed` fires only when the server confirms this call claimed the reward, so a reward is credited once across devices.

## Ads and portal

| Type | Kind | Purpose |
|---|---|---|
| `IAdsProvider` | Interface | `IsAvailable`, `IsRewardedReady`, `IsAdShowing`, `ShowRewarded`, `ShowInterstitial`, `ReportScore`, `Tick` |
| `AdResult` | Enum | `Completed` (grant the reward), `Failed`, `Unavailable` |
| `AdsDebug` | Static class | Last 12 ad events for an on-screen overlay (`Lines`, `Changed`); also written to the console |
| `IPortalLocale` | Interface | `TryGetLocale(out twoLetterCode)` — the language the portal chose |
| `NoPortalLocale` | Sealed class | `IPortalLocale` that always returns `false` |

- `onComplete` is called exactly once per show.
- Call `Tick()` every frame: portals do not report a show that never happened, so providers time out stale requests themselves.
- Gate code that runs on unscaled time with `IsAdShowing`; the portal pauses only scaled time and audio.

## Editor: project options

`LongLeap.Core.Editor` is a small framework that lets packages describe build-affecting project settings as data. The Build Manager window (`com.longleap.buildmanager`) and package menus read the same descriptions, so they always agree.

Packages contribute by implementing a provider interface with a public parameterless constructor. Providers are discovered with `TypeCache`, so the order assemblies load in does not matter. Do not query the registries from `[InitializeOnLoad]` code.

| Provider interface | Supplies | Registry |
|---|---|---|
| `IProjectOptionSectionProvider` | `ProjectOptionSection` — a titled group of options and actions | `ProjectOptionRegistry` |
| `IProjectOptionPresetProvider` | `ProjectOptionPreset` — a named set of option values | `ProjectOptionPresets` |
| `IProjectBuildTargetProvider` | `ProjectBuildTarget` — one way to build; highest applicable `Priority` wins | `ProjectBuildTargets` |
| `IProjectWarningProvider` | `ProjectWarning` — a configuration problem, optionally blocking the build and offering a fix | `ProjectWarnings` |

Option kinds (every value is a string: `"1"`/`"0"` for toggles, an enum member name for choices):

- `DefineOption` — a scripting define symbol; can switch dependent symbols off with it (`cascadeOffSymbols`).
- `AssetEnumOption` — an enum field in an asset, written through `SerializedObject` by field name.
- `CustomOption` — arbitrary read/write delegates (e.g. patching a vendor plugin's asmdefs).

Options can declare availability (with a mandatory reason when blocked), a confirmation dialog, and notes logged after applying. Writes go through `ProjectWriteContext`, which applies all asset changes and one define change in a single `Flush()`, so applying many options costs one recompile. `ProjectOptionMenu` wires an option to a `[MenuItem]` in one line.

> **WebGL only.** `ScriptingDefines` reads and writes defines for `NamedBuildTarget.WebGL`.

```csharp
public sealed class MyOptions : IProjectOptionSectionProvider
{
    public IEnumerable<ProjectOptionSection> GetSections()
    {
        yield return new ProjectOptionSection("my-package", "My Package", order: 100, new ProjectOption[]
        {
            new DefineOption("my-debug", "MY_DEBUG", "Debug overlay",
                "Shows the debug overlay. Turn off before release.", required: false, order: 0),
        });
    }
}
```

## Tests

EditMode tests live in `Tests/EditMode` (assembly `LongLeap.Core.Tests`, requires `UNITY_INCLUDE_TESTS`).
