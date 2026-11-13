# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-13

First public release, under the MIT license.

## [0.1.0] - 2026-11-05

First release.

### Added

- `Setting`, `Setting<T>`, `NumericSetting<T>` (clamped to `Min`/`Max`) and
  `EnumSetting<T>` (option labels through `Loc`, selection through `IEnumSetting`).
- `ISettingsGroup`: the unit of persistence; each group serializes itself to one
  string under its `StorageKey`.
- `ISettingsService` / `SettingsService`: group registration (duplicate storage keys
  throw), `SetValue` with clamping, equality check and `OnChanged`, and
  `LoadAllAsync` / `SaveAllAsync` over `ICloudSaveService`. A null cloud save means
  no I/O; a group that fails to load keeps its defaults.
- `SettingsServiceExtensions`: `FindSetting(key)` and `ResetToDefaults()`.
- `SettingsPanel` (assembly `LongLeap.Settings.UI`): settings UI built in code from a
  `SettingsWindowConfig`, picked per device profile by `SettingsWindowConfigSet`.
  Rows: `SettingsVolumeRow` (numeric slider), `SettingsToggleRow` (bool),
  `SettingsKeyRow` (key rebinding; a key in use is swapped with the other action).
- `[SettingKey]` inspector dropdown listing every setting key in the project.
- EditMode tests.
