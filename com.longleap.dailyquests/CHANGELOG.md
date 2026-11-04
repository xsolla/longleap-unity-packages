# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-11-04

### Changed (breaking)

- `QuestRewardKey` removed: `QuestReward.Key` is now `LongLeap.Core.RewardKey`,
  shared with daily rewards. Replace `new QuestRewardKey(...)` with
  `new RewardKey(...)` and add `using LongLeap.Core;`.
- The internal `ResetSchedule` moved to `LongLeap.Core.ResetSchedule`; its tests
  moved to the core package.

### Dependencies

- Requires `com.longleap.core` 0.2.0.

## [0.2.0]

Earlier history is not recorded here.
