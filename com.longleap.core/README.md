# LongLeap Core

Foundation package for LongLeap. It holds:

- the **contracts** other packages implement (auth, cloud saves, ads, storage, A/B experiments);
- the **backend-agnostic service** built on them: `CloudSaveService`;
- small **shared types** (`RewardKey`, `ResetSchedule`) and the Unity implementations of the storage contracts.

Elsewhere: leaderboards and leagues are in `com.longleap.xsollaid` (`LongLeap.XsollaId.Leaderboards` / `.Leagues`, only with `XSOLLA_ID`); the Editor framework for project options and build presets is in `com.longleap.buildmanager` (`LongLeap.BuildManager.Options`).

Backends live in other packages: `com.longleap.firebase`, `com.longleap.xsollaid`, `com.longleap.gamepix`, and null backends in `com.longleap.nullproviders`. Terms are defined in [CONTEXT.md](CONTEXT.md).

Requires Unity 6000.0+ and `com.unity.nuget.newtonsoft-json`.

---

## Install (Unity Package Manager)

Unity 6 (6000.0 – 6000.4), WebGL build target. In Unity: **Window → Package Manager → + → Add package from git URL**. This package needs no other LongLeap package.

```
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.core
```

Pin a version by adding `#v1.0.0` at the end of each URL. Unity packages this one uses are installed automatically. All packages and their dependencies: [root README](../README.md#install-unity-package-manager).

## Contents

| Module | Namespace | Defines | Implemented elsewhere |
|---|---|---|---|
| Storage | `LongLeap.Core` | `IPlayerPrefs`, `IPersistentStorage` + 4 Unity implementations | — |
| Experiments | `LongLeap.Core` | `IExperimentInfoProvider`, `AbExperimentInfo` | `RemoteConfigService` (remoteconfig), read by analytics |
| Shared types | `LongLeap.Core` | `RewardKey`, `ResetSchedule` | used by daily rewards and daily quests |
| Auth | `LongLeap.Core.Auth` | `IAuthProvider`, `AuthResult`, `UserId`, `IdToken` | null, Firebase, Xsolla ID |
| Cloud saves | `LongLeap.Core.CloudSaves` | `ICloudSaveService`, `ICloudSaveProvider`, `CloudSaveService`, scope/batch/bulk interfaces | null, Firebase, Xsolla ID |
| Ads / portal | `LongLeap.Core.Ads` | `IAdsProvider`, `AdResult`, `AdsDebug`, `IPortalLocale` | null (`NullAdsProvider`, `NullPortalLocale`), GamePix |
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

## Ads and portal

| Type | Kind | Purpose |
|---|---|---|
| `IAdsProvider` | Interface | `IsAvailable`, `IsRewardedReady`, `IsAdShowing`, `ShowRewarded`, `ShowInterstitial`, `ReportScore`, `Tick` |
| `AdResult` | Enum | `Completed` (grant the reward), `Failed`, `Unavailable` |
| `AdsDebug` | Static class | Last 12 ad events for an on-screen overlay (`Lines`, `Changed`); also written to the console |
| `IPortalLocale` | Interface | `TryGetLocale(out twoLetterCode)` — the language the portal chose |

- `onComplete` is called exactly once per show.
- Call `Tick()` every frame: portals do not report a show that never happened, so providers time out stale requests themselves.
- Gate code that runs on unscaled time with `IsAdShowing`; the portal pauses only scaled time and audio.

## Tests

EditMode tests live in `Tests/EditMode` (assembly `LongLeap.Core.Tests`, requires `UNITY_INCLUDE_TESTS`).
