# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-13

First public release, under the MIT license.

## [0.1.0] - 2026-11-04

First release.

### Added

- `Loc` static facade: `Init`, `Reset`, `IsReady`, `Resolve`, `ResolveFormat`,
  `OnLanguageChanged`. Returns keys as-is when no service is attached, and clears
  its static state on Play Mode entry when domain reload is disabled.
- `ILocalizationService` contract for plugging in any string source.
- `LocalizationService`, backed by Unity Localization string tables:
  - index-based table cache that is safe in player builds
  - locale switching through `SwitchLanguage` that keeps the previous strings
    until the new tables load, and ignores stale reloads
  - `CurrentLocaleCode`, `GetAvailableLocaleCodes`, `LoadTables` for preloading,
    and `IDisposable`
  - missing keys logged once per key; malformed format strings do not throw
- `LocKey` serializable key with implicit string conversions and `Format`.
  Null and empty strings are the same empty key.
- `LocalizedText` component for `TMP_Text` and `UI.Text`. It keeps the prefab
  text until localization is ready, and `SetKey(null)` clears the text.
