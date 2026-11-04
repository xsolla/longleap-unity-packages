# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `UnityPlayerPrefsAdapter`, `PlayerPrefsPersistentStorage` and `FilePersistentStorage`
  in the `LongLeap.Core` namespace, moved from the removed `com.longleap.platform.unity`
  package (was `LongLeap.Platform.Unity`). Behaviour is unchanged; replace
  `using LongLeap.Platform.Unity;` with `using LongLeap.Core;`.

### Removed

- `LongLeap.Core.Ads.NoAdsProvider`. Use `LongLeap.Null.NullAdsProvider` from
  `com.longleap.nullproviders` instead (same behaviour). Release together with
  `com.longleap.nullproviders` 0.1.0.
