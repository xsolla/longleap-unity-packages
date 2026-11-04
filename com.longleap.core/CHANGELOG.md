# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
