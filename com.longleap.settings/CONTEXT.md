# LongLeap Settings

Typed settings system with UI controls and cloud persistence. Built on `ICloudSaveService` — null-safe (skip I/O if service is absent).

**Setting**:
A single typed preference value. Wraps a key, label key, default value, and current runtime value. Four variants: `Setting<bool>` (toggle), `NumericSetting<float>` (float slider), `NumericSetting<int>` (int slider), `EnumSetting<T>` (enum dropdown).
_Avoid_: Option, pref, config value

**Numeric Setting**:
A setting with a `Min`/`Max` range. Values set outside the range are clamped to the nearest bound via `Coerce`.
_Avoid_: Ranged setting, bounded value

**Enum Setting**:
A setting backed by a C# enum. Automatically generates dropdown options from `Enum.GetValues`, resolves option labels via `Loc.Resolve($"{LabelKey}_{name.ToLower()}")`. Dropdown selection dispatches through `IEnumSetting` — no reflection.
_Avoid_: Choice setting, picker setting

**Settings Group**:
A collection of related settings with a shared `StorageKey`. The unit of cloud persistence — each group serializes to its own JSON blob under its `StorageKey`. Has a `DisplayNameKey` used for section headers in the settings panel.
_Avoid_: Settings section, config group

**Settings Service**:
Orchestrator: registers groups, loads stored values on boot, saves on window close, fires `OnChanged` when a value changes.
_Avoid_: Settings manager, preferences system

**Settings Panel**:
Plain class (not a MonoBehaviour) that builds the settings UI in code inside a given `RectTransform`. What it shows comes from a **Settings Window Config**; it is not tied to a window system — the game's window owns it and calls `Bind` / `Refresh` / `StopListening`.
_Avoid_: Settings screen, preferences window

**Settings Window Config**:
ScriptableObject listing sections and rows (by setting key) plus captions, font and panel size. A `SettingsWindowConfigSet` holds one per device profile (Desktop / Mobile / WebDesktop / WebMobile); an empty profile falls back to the nearest filled one.
_Avoid_: Settings layout, settings schema

**Settings Row**:
A code-built UI row bound to a single setting; the row type follows the setting type: `SettingsVolumeRow` (numeric, extends `SettingsSliderBase`), `SettingsToggleRow` (bool), `SettingsKeyRow` (key rebind). Enum settings have no row yet.
_Avoid_: Settings widget, settings control

## Relationships

- **Settings → Cloud Saves**: `SettingsService` uses `ICloudSaveService` for per-group JSON persistence.
- **Settings → Localization**: `EnumSetting<T>` uses `Loc.Resolve` for dropdown option labels; rows use `Loc` for their labels.
- **Settings → Tools**: the panel picks its config by `DeviceInfo.Profile` and builds UI with `RuntimeUi` (assembly `LongLeap.Tools.UI`).
