# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-11-04

First release.

### Added

- `NullAuthProvider` — `IAuthProvider` that always logs in with no token and a fixed user id (`"anonymous"` by default, configurable; null or empty ids are rejected).
- `NullCloudSaveProvider` — `ICloudSaveProvider` and `IAccountCloudSaveScope` that store nothing; guest scope only.
- `NullLeaderboardProvider` — `ILeaderboardProvider` that accepts submits and returns empty boards.
- `NullLeagueProvider` — `ILeagueProvider` that reports a league the player has not joined, with no rewards.
- `NullAdsProvider` — `IAdsProvider` for builds without an ad network: every request completes with `AdResult.Unavailable`.
