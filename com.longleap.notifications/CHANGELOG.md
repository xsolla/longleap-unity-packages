# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `CoroutineTimer` in the `LongLeap.Notifications` namespace, moved from the removed
  `com.longleap.platform.unity` package (was `LongLeap.Platform.Unity`). Drop
  `using LongLeap.Platform.Unity;`.
- PlayMode tests for `CoroutineTimer`.

### Changed

- `CoroutineTimer` validates its input: a null host or callback throws
  `ArgumentNullException`, starting on a destroyed host or an inactive GameObject throws
  `InvalidOperationException` (previously Unity logged an error and the timer never
  fired), and a negative duration counts as zero.
