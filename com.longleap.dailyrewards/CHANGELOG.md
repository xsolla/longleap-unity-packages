# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-13

First public release, under the MIT license.

### Changed

- CONTEXT.md brought up to date: no cycle-completed event since 0.2.0 (the claim
  result carries `CompletedCycle`); reset time math lives in
  `LongLeap.Core.ResetSchedule` and `DailyRewardsRules`.

## [0.2.0] - 2026-11-04

Reliability and architecture pass. **Breaking**: callers, game bridges, configs
and state stores need updating (see Migration below).

### Changed (breaking)

- `IDailyRewardsService.Claim()` -> `Task<bool> ClaimAsync()`. The claimed day is
  persisted **before** the reward is granted; if the save fails nothing is granted,
  state is untouched and `false` is returned. While a save is in flight
  `CanClaimToday` is `false`, so a second press is rejected.
- `IDailyRewardsStateStore.LoadAsync()` returns `DailyRewardsStateLoadResult`
  (`Loaded` / `Empty` / `Unreadable` / `Unavailable`) instead of a state or `null`.
- `IDailyRewardsGameBridge` has one method, `ApplyReward`. `OnStreakChanged`,
  `OnCycleCompleted` and `OnStreakReset` are gone; the same information is on
  `DailyRewardClaimResult` (`StreakWasReset`, `CompletedCycle`, `CycleNumber`).
- `IDailyRewardsConfig`: `DayCount` removed — the number of days is
  `Days.Count`, a day's index is its position. `Days` is `IReadOnlyList<DailyRewardDay>`;
  `DailyRewardDay.Rewards` is `IReadOnlyList<RewardEntry>`.
- `DefaultDailyRewardConfigSO`: `_dayCount` and per-day `_dayIndex` fields removed.
  An empty `Days` list disables the calendar.
- `Reset()` and `DebugSkipToNextResetWindow()` moved off the game-facing service to
  `IDailyRewardsDebug` (`ResetState()`, `SkipToNextResetWindow()`).
- `RewardKey` moved to `LongLeap.Core` (shared with daily quests).
- Save format v2: `DailyRewardsState.ClaimedCount` replaces the `ClaimedDayIndices`
  array. v1 saves are migrated on load. A client older than 0.2.0 reads a v2 save
  as unreadable and starts the calendar over.
- `Loop` policy: the finished cycle stays visible (all days `Claimed`) until the next
  reset instead of being cleared right after the last claim.

### Added

- `IDailyRewardsService.Changed` event, `Tick()` (raises `Changed` when a new reset
  window starts), `TimeUntilNextClaim`, `IsReady`.
- Injectable clock: `DailyRewardsService(..., Func<DateTime> utcNow = null)`.
- `DailyRewardsConfigValidator`, run by the service at init and by the SO in `OnValidate`.
- `JsonDailyRewardsStateStore.Parse(json)` — shared parsing with version check and
  migration, reusable by other stores (e.g. cloud saves).
- `DailyRewardsState.Empty()` and `CurrentVersion`.

### Fixed

- A failed save no longer leaves a granted reward with an unsaved claim (double
  reward after restart); an exception in the bridge no longer aborts the claim halfway.
- Saves are serialized: an older state can no longer overwrite a newer one.
- A save from a newer version is no longer treated as empty and overwritten by
  `JsonDailyRewardsStateStore`.
- The calendar no longer gets stuck in `Loop` when the config gets shorter than the
  saved progress.
- `Days` of the config SO is built once instead of on every access.

### Internal

- Calendar rules extracted to `DailyRewardsRules` (pure, unit-tested); reset time math
  uses `LongLeap.Core.ResetSchedule`.
- Local `IsExternalInit` polyfill removed (provided by `LongLeap.Core`).

### Migration

- Call `await service.ClaimAsync()`; skip registering the service when `IsReady` is false.
- Bridge: implement only `ApplyReward`; read streak/cycle info from the result.
- Config: drop `DayCount`; return `Days` as `IReadOnlyList`.
- Store: return `DailyRewardsStateLoadResult`; parse with `JsonDailyRewardsStateStore.Parse`.
- UI: subscribe to `Changed`, call `Tick()` from `Update` while open; `Rewards.Length` -> `.Count`.
- `using LongLeap.Core;` for `RewardKey`.

## [0.1.0]

First release.
