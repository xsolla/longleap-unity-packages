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
MonoBehaviour that auto-builds settings UI. Iterates `ISettingsService.Groups`, instantiates control prefabs (slider/toggle/dropdown), wires `Bind()` calls. Uses abstract control bases for prefab references.
_Avoid_: Settings screen, preferences window

**Settings Control**:
A UI widget bound to a single setting. Three concrete types: `SettingsSlider` (extends `SettingsSliderBase`), `SettingsToggle` (extends `SettingsToggleBase`), `SettingsDropdown` (extends `SettingsDropdownBase`).
_Avoid_: Settings widget, pref row

**Control Base**:
Abstract MonoBehaviour class declaring the `Bind()` contract. Prefab slots on `SettingsPanel` are typed to bases, not concretes — visual implementations can be swapped by replacing prefabs.
_Avoid_: Base widget, abstract control

## Relationships

- **Settings → Cloud Saves**: `SettingsService` uses `ICloudSaveService` for per-group JSON persistence.
- **Settings → Localization**: `EnumSetting<T>` uses `Loc.Resolve` for dropdown option labels; `SettingsPanel` uses `Loc` for group section headers and control labels.
