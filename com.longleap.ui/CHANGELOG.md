# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-13

First public release, under the MIT license.

### Added

- Notification core (`LongLeap.Notifications` assembly, `Runtime/Notifications/Core`),
  moved from the removed `com.longleap.notifications` package. Namespaces and assembly
  names are unchanged; replace the `com.longleap.notifications` dependency with
  `com.longleap.ui`.
- `CoroutineTimer` in `LongLeap.Notifications`, moved from the removed
  `com.longleap.platform.unity` package (was `LongLeap.Platform.Unity`). Drop
  `using LongLeap.Platform.Unity;`.
- PlayMode tests for `CoroutineTimer` (`LongLeap.Notifications.PlayModeTests`).
- PlayMode tests for `WindowService` (`LongLeap.UI.Tests`): unknown windows, catalog
  validation, home window, EventSystem ownership.
- `LICENSE.md` and package metadata (`author`, `license`, documentation / changelog / license
  URLs).

### Changed

- `CoroutineTimer` validates its input: a null host or callback throws
  `ArgumentNullException`, starting on a destroyed host or an inactive GameObject throws
  `InvalidOperationException` (previously Unity logged an error and the timer never
  fired), and a negative duration counts as zero.
- `LongLeap.UI.Notifications` always compiles; it no longer needs the
  `LONGLEAP_NOTIFICATIONS` / `LONGLEAP_PLATFORM_UNITY` defines.
- All code comments, Inspector tooltips and headers, and log / exception messages are in
  English (were Russian). Log tags (`[Windows]`, `[BackPack]`, …) are unchanged.
- Notification tests merged where one test was a subset of another (priority and FIFO
  ordering in one test, zero / negative duration as test cases); coverage is unchanged.
- README: corrected the dependency list (`com.longleap.tools` is required), added a Tests
  section, and rewrote the migration notes.
