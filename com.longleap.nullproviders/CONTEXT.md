# LongLeap Null Providers

Null-object backends for the service seams in `com.longleap.core`. Each does nothing, makes no network calls, keeps no state and completes synchronously with a safe default. Used for guest play, offline builds, builds without ads and tests. Shipped in Runtime; always explicitly chosen at construction, never a silent fallback.

**NullAuthProvider** — `IAuthProvider`. Login succeeds with `IdToken.None` and a fixed `UserId` (`"anonymous"` by default, or the id given to the constructor).

**NullCloudSaveProvider** — `ICloudSaveProvider` + `IAccountCloudSaveScope`. Stores nothing: writes are dropped, reads return empty. Guest scope only: `UseAccount` is ignored, so never pair it with a signed-in account.

**NullLeaderboardProvider** — `ILeaderboardProvider`. Submits report success; boards are empty; rank is null with 0 players.

**NullLeagueProvider** — `ILeagueProvider`. Empty, not-joined state; no rewards; never throws.

**NullAdsProvider** — `IAdsProvider`. No ads: not available, every request completes at once with `AdResult.Unavailable`.

## Relationships

- **Null → Core**: implements provider interfaces from `com.longleap.core`. That is the only dependency.
- Null backends of feature seams live in their own packages: `NullRemoteConfigProvider` (remoteconfig), `NullAnalyticsBackend` (analytics), `NullBackPackService` (backpack), the customization `Null…Service` classes.

## Rejected names

*Stub*, *Mock*, *Fake* (those are test doubles; these ship in builds), *Offline* (implies local persistence, which these do not have), *NoAds* (use the `Null` prefix for every seam).
