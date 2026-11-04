# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `JsonAchievementProvider` and `ResourcesAchievementProvider` in the
  `LongLeap.Achievements` namespace, moved from the removed `com.longleap.platform.unity`
  package (was `LongLeap.Platform.Unity`). Behaviour is unchanged; drop
  `using LongLeap.Platform.Unity;`.
- Dependency on `com.unity.nuget.newtonsoft-json` 3.0.2 (used by `JsonAchievementProvider`).
