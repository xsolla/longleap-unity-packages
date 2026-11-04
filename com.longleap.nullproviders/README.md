# LongLeap Null Providers

`com.longleap.nullproviders` · assembly and namespace `LongLeap.Null`

Null-object backends for the service seams defined in `com.longleap.core`. Each one
does nothing, makes no network calls, keeps no state and completes synchronously
with a safe default. The services on top keep working without null checks or
special cases.

A null provider is **always chosen explicitly** when the service is constructed.
It is never a silent fallback for a real backend that failed.

## Public API

| Type | Implements | Behaviour |
|---|---|---|
| `NullAuthProvider` | `IAuthProvider` | `LoginAsync()` succeeds with `IdToken.None` and a fixed user id: `"anonymous"` (`DefaultUserId`) or the id passed to the constructor. |
| `NullCloudSaveProvider` | `ICloudSaveProvider`, `IAccountCloudSaveScope` | Writes and deletes are dropped; reads return null / false / empty. Never account-scoped: `UseAccount` / `UseGuest` are no-ops, `PeekAccountAsync` returns an empty save. |
| `NullLeaderboardProvider` | `ILeaderboardProvider` | Submits return `true` and are dropped; top and surroundings are `LeaderboardPage.Empty()`; rank is `PlayerRank(null, null, 0)`. |
| `NullLeagueProvider` | `ILeagueProvider` | Every read and submit returns an empty, not-joined state in the lowest tier with period 0; claims return `LeagueClaim(false, 0)`; debug calls do nothing. |
| `NullAdsProvider` | `IAdsProvider` | Not available, never ready or showing. `ShowRewarded` / `ShowInterstitial` call back once, synchronously, with `AdResult.Unavailable` and log to `AdsDebug`. |

The null backends for other seams live next to their interfaces, so this package
does not pull in feature packages:

| Seam | Null implementation | Package |
|---|---|---|
| `IRemoteConfigProvider` | `NullRemoteConfigBackend` | `com.longleap.remoteconfig` |
| `IAnalyticsBackend` | `NullAnalyticsBackend` | `com.longleap.analytics` |
| `IBackPackService` | `NullBackPackService` | `com.longleap.backpack` |
| `IColorService`, `IEquipmentService`, `IInventoryService` | `Null…Service` | `com.longleap.customization` |

## Usage

Each provider is independent, so any mix works: for example real auth with null
cloud saves. A guest player whose progress stays on this device:

```csharp
using LongLeap.Core.CloudSaves;
using LongLeap.Null;

ICloudSaveProvider provider = isSignedIn
    ? new FirebaseCloudSaveProvider(http, auth, prefs)
    : new NullCloudSaveProvider();

var saves = new CloudSaveService(provider, prefs);
```

A build without an ad network:

```csharp
IAdsProvider ads = isPortalBuild ? new GamePixAdsProvider(...) : new NullAdsProvider();
```

## Notes

- **Shared identity.** Every `NullAuthProvider` built with the default id reports
  the same user, so all such players share one identity (for example one
  leaderboard entry). Pass a per-device id when that matters.
- **Cloud saves are guest-only.** With `NullCloudSaveProvider` the data lives only
  in the local cache of `CloudSaveService` on this device. `UseAccount` is
  ignored and `IsAccountScoped` is always `false`, so never use it for a
  signed-in account: the save switcher would merge guest data into the account
  again on every launch. Use a real provider (Firebase, Xsolla) for accounts.
- **Leagues.** `ILeagueProvider` normally throws on a failed request. The null
  provider never throws: it reports a league the player has not joined.
- **Platforms.** No Unity APIs besides `AdsDebug` logging, no platform code. Safe
  for WebGL, Editor and standalone builds.
