# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-13

First public release, under the MIT license.

### Changed

- README translated into English and brought up to date: dependencies listed in
  Setup, `ResetSchedule` referred to as `LongLeap.Core.ResetSchedule`.

## [0.3.0] - 2026-11-04

### Changed (breaking)

- `QuestRewardKey` removed: `QuestReward.Key` is now `LongLeap.Core.RewardKey`,
  shared with daily rewards. Replace `new QuestRewardKey(...)` with
  `new RewardKey(...)` and add `using LongLeap.Core;`.
- The internal `ResetSchedule` moved to `LongLeap.Core.ResetSchedule`; its tests
  moved to the core package.

### Dependencies

- Requires `com.longleap.core` 0.2.0.

## [0.2.0] - 2026-11-03

Reliability pass and selection rules. **Breaking**: callers, game bridges, state
stores and config providers need updating.

### Changed (breaking)

- `IDailyQuestService.Claim(id)` -> `Task<bool> ClaimAsync(id)`. The claim is
  persisted **before** `ApplyRewards` is called; if the save fails nothing is
  granted and `false` is returned, so the player can press again.
- Debug methods moved off the game-facing service to `IDailyQuestDebug`:
  `ForceComplete`, `SkipToNextPeriod` (was `DebugSkipToNextResetWindow`),
  `ResetState` (was `Reset`). `DailyQuestService` implements both interfaces;
  register it as `IDailyQuestDebug` for dev tools.
- `IDailyQuestStateStore.LoadAsync()` returns `DailyQuestStateLoadResult`
  (`Loaded` / `Empty` / `Unreadable` / `Unavailable`) instead of a state or `null`.
  `JsonDailyQuestStateStore.Parse(json)` returns the same result type.
- `IDailyQuestGameBridge` is required: `DailyQuestService` throws
  `ArgumentNullException` without it (a claimed reward would otherwise be lost).
- `StreamingAssetsDailyQuestProvider` removed. Use `ResourcesDailyQuestProvider`:
  it reads `Resources/DailyQuests/daily_quests.json` as a `TextAsset`, with no HTTP,
  so it works on portals that repackage the build (GamePix). Move the JSON from
  `StreamingAssets/DailyQuests/` to `Resources/DailyQuests/`.

### Added

- Selection rules: `settings.uniqueCategories`, `settings.hardestLast`,
  `settings.slotDifficulties`, plus `quests[].categories` and `quests[].difficulty`
  (`DailyQuestSettings.HasSelectionRules`, `QuestDefinition.Categories` /
  `Difficulty`). With rules the set is ordered easy to hard. When a full set cannot
  meet every rule, they are relaxed in order: unique categories, then difficulty,
  then all rules. The search is bounded by `DailyQuestSelector.SearchBudget`.
- `DailyQuestValidator` warns when the rules cannot be met on the pool: a slot asks
  for a difficulty no quest has, more slots than `dailyCount`, no full set possible.
- `IDailyQuestService.Tick()`: checks for a new period and raises `Changed` when
  the set is replaced. Call it from `Update` while the quest UI is open.
- Per-objective completion latch (`ObjectiveBaseline.IsCompleted`): a met objective
  stays met even if its stat drops later.
- Rebase: when a `Delta` stat drops below its baseline (rolled-back stats save),
  the baseline is lowered to the current value and saved.
- An unreadable save (corrupted or from a newer version) withholds the set until
  the next reset (`DailyQuestsState.Withheld`) instead of starting over, so rewards
  claimed today cannot be claimed again.
- When `dailyCount` grows mid-period the set is topped up by the same rules;
  remaining quests keep their slots and categories.

### Changed

- Saves are queued: each write waits for the previous one, so an older state never
  overwrites a newer one.
- Stat changes only re-check quests that wait on that stat and allocate nothing when
  nothing moved. `Changed` fires only when visible progress or state changed.
- `GetActive()` returns the same array and `QuestStatus` instances until something
  changes (compare by reference to skip redraws); `QuestStatus` is rebuilt lazily.
  `GetActive()` also reconciles progress with stats changed without an event.
- Quest categories are trimmed; empty ones are dropped.
- Logic split into `ResetSchedule`, `ProgressEvaluator` and `DailyQuestSelector`,
  each tested on its own.
- Code comments and log messages are in English.

### Save format

- Still version 1. New fields `DailyQuestsState.Withheld` and
  `ObjectiveBaseline.IsCompleted` default to `false`, so 0.1.0 saves load as is.

## [0.1.0] - 2026-10-08

First release: a JSON-defined quest pool, a weighted deterministic daily set per UTC
reset window, progress derived from `IStatSystem` (`delta` / `absolute`), rewards
claimed manually through `IDailyQuestGameBridge.ApplyRewards`, state persisted via
`JsonDailyQuestStateStore`.
