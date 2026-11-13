# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-13

First public release, under the MIT license.

### Added

- `UnityPlayerPrefsAdapter`, `PlayerPrefsPersistentStorage` and `FilePersistentStorage`
  in the `LongLeap.Core` namespace, moved from the removed `com.longleap.platform.unity`
  package (was `LongLeap.Platform.Unity`). Behaviour is unchanged; replace
  `using LongLeap.Platform.Unity;` with `using LongLeap.Core;`.
- `InMemoryPlayerPrefs`: dictionary-backed `IPlayerPrefs` for tests, moved from
  `com.longleap.remoteconfig` (was `LongLeap.RemoteConfig.Storage`). Replace
  `using LongLeap.RemoteConfig.Storage;` with `using LongLeap.Core;`. Integers are
  now written and read culture-invariantly.

### Changed

- All code comments, exception messages and test messages are now in English.
  Exception texts of `IdToken`, `UserId` and `CloudSaveKey` changed accordingly.
- README rewritten to cover every module (leagues, ads/portal, shared types)
  and to match current behaviour. CONTEXT.md updated.

### Removed

- Leaderboards (`LongLeap.Core.Leaderboards`) and leagues (`LongLeap.Core.Leagues`):
  contracts, services and value types moved to `com.longleap.xsollaid` as
  `LongLeap.XsollaId.Leaderboards` / `LongLeap.XsollaId.Leagues` (assembly
  `LongLeap.XsollaId.Web`, only with `XSOLLA_ID`). Same types, same behaviour.
  Replace the `using` lines and reference `LongLeap.XsollaId.Web`. Their tests moved too.
- `LongLeap.Core.Editor` (assembly and namespace): the project options framework moved to
  `com.longleap.buildmanager` as `LongLeap.BuildManager.Options` (same types, same
  behaviour). In asmdefs reference `LongLeap.BuildManager.Options`, replace
  `using LongLeap.Core.Editor;` with `using LongLeap.BuildManager.Options;`, and depend on
  `com.longleap.buildmanager`.
- `LongLeap.Core.Ads.NoPortalLocale`. Use `LongLeap.Null.NullPortalLocale` from
  `com.longleap.nullproviders` instead (same behaviour).
- `LongLeap.Core.Ads.NoAdsProvider`. Use `LongLeap.Null.NullAdsProvider` from
  `com.longleap.nullproviders` instead (same behaviour). Release together with
  `com.longleap.nullproviders` 0.1.0.

## [0.2.0] - 2026-11-04

### Added

- `RewardKey` (`Rewards/RewardKey.cs`): reward key newtype shared by daily rewards
  and daily quests (replaces `LongLeap.DailyRewards.RewardKey` and
  `LongLeap.DailyQuests.QuestRewardKey`). The game decides what a key means.
- `ResetSchedule` (`Time/ResetSchedule.cs`): daily reset period math (period start,
  next reset, time until reset) for a UTC reset hour; moved from daily quests
  (where it was internal) so daily rewards can share it.

## [0.1.0]

First release. Earlier history is not recorded here.
