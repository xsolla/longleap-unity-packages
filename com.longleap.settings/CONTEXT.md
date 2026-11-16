# LongLeap Settings

Typed settings system with UI controls and cloud persistence. Built on `ICloudSaveService` — null-safe (skips I/O if the service is absent).

**Setting**:
A single typed preference value. Wraps a key, label key, default value, and current runtime value. Variants: `Setting<bool>` (toggle), `NumericSetting<float>` (float slider), `NumericSetting<int>` (int slider), `Setting<Key>` (key binding), `EnumSetting<T>` (enum options).
_Avoid_: Option, pref, config value

**Numeric Setting**:
A setting with a `Min`/`Max` range. Values set through the service outside the range are clamped to the nearest bound via `Coerce`.
_Avoid_: Ranged setting, bounded value

**Enum Setting**:
A setting backed by a C# enum. Option labels are generated from `Enum.GetNames` and resolved via `Loc.Resolve($"{LabelKey}_{name.ToLowerInvariant()}")`. Selection is exposed through `IEnumSetting` — no reflection.
_Avoid_: Choice setting, picker setting

**Settings Group**:
A collection of related settings with a shared `StorageKey`. The unit of cloud persistence — each group serializes to its own string (usually JSON) under its `StorageKey`. Has a `DisplayNameKey` for a section name (not used by the panel, which takes section titles from its config).
_Avoid_: Settings section, config group

**Settings Service**:
Orchestrator: registers groups, loads and saves stored values on request, fires `OnChanged` when a value changes. It does not decide when to save — the game does (typically on settings window close).
_Avoid_: Settings manager, preferences system

**Settings Panel**:
Plain class (not a MonoBehaviour) that builds the settings UI in code inside a given `RectTransform`. What it shows comes from a **Settings Window Config**; it is not tied to a window system — the game's window owns it and calls `Bind` / `Refresh` / `StopListening` / `Dispose`.
_Avoid_: Settings screen, preferences window

**Settings Window Config**:
ScriptableObject listing sections and rows (by setting key) plus captions, font and panel size. A `SettingsWindowConfigSet` holds one per device profile (Desktop / Mobile / WebDesktop / WebMobile); an empty profile falls back to the nearest filled one.
_Avoid_: Settings layout, settings schema

**Settings Row**:
A code-built UI row bound to a single setting; the row type follows the setting type: `SettingsVolumeRow` (numeric, extends `SettingsSliderBase`), `SettingsToggleRow` (bool), `SettingsKeyRow` (key rebind). Enum settings have no row yet.
_Avoid_: Settings widget, settings control

## Relationships

- **Settings → Cloud Saves**: `SettingsService` uses `ICloudSaveService` (`com.longleap.core`) for per-group persistence.
- **Settings → Localization**: `EnumSetting<T>` uses `Loc.Resolve` for option labels; rows use `Loc` for their labels and fall back to the config label.
- **Settings → Tools**: the panel picks its config by `DeviceInfo.Profile` and builds UI with `RuntimeUi` (assembly `LongLeap.Tools.UI`).
- **UI → Settings**: `com.longleap.ui`'s `SettingsWindow` hosts a `SettingsPanel`.
