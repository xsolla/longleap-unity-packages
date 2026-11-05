# LongLeap Settings

Typed settings system. Define settings in code as group classes; values persist to cloud via `ICloudSaveService`; the settings UI is built in code by `SettingsPanel` from a `SettingsWindowConfig` asset (see [Config-driven settings panel](#config-driven-settings-panel)).

## Assemblies

| Assembly | Folder | Contents |
|---|---|---|
| `LongLeap.Settings` | `Runtime/` | Settings, groups, service, extensions. Depends on `LongLeap.Core` (cloud save) and `LongLeap.Localization` (enum option labels) |
| `LongLeap.Settings.UI` | `Runtime/Panel/` | `SettingsPanel`, rows, window configs. Adds uGUI, TextMeshPro, Input System and `LongLeap.Tools` / `LongLeap.Tools.UI` |
| `LongLeap.Settings.UI.Editor` | `Editor/` | `[SettingKey]` inspector dropdown |
| `LongLeap.Settings.Tests` | `Tests/EditMode/` | EditMode tests |

## Data Sources

Cloud save, one string per group's `StorageKey`. Each group decides its own format; JSON is the usual choice. Example for a float slider group:

```json
// Cloud key: "settings.audio"
{
  "master_volume": 1.0,
  "sfx_volume":    0.8,
  "music_volume":  0.5
}
```

Groups define their own field names via `Serialize()` / `Deserialize()`. They are not tied to setting keys: a setting with `Key = "audio.master_volume"` can serialize as `"master_volume"`.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `Setting` | Abstract class | Base: `Key`, `LabelKey`, `ValueType`, `BoxedValue` |
| `Setting<T>` | Class | Typed wrapper: `DefaultValue`, `Value` (public get/set) |
| `NumericSetting<T>` | Class (`where T : struct`) | `Min`, `Max`. Values set through the service are clamped to the range |
| `EnumSetting<T>` | Class (`where T : struct, Enum`) | Option labels via `Loc.Resolve($"{LabelKey}_{name.ToLowerInvariant()}")`. Implements `IEnumSetting` explicitly |
| `IEnumSetting` | Interface | Untyped access for UI: `GetOptionLabels()`, `GetOptionIndex()`, `ApplyIndex(index, service)` |
| `ISettingsGroup` | Interface | `StorageKey`, `DisplayNameKey`, `All`, `Serialize()`, `Deserialize()` |
| `ISettingsService` | Interface | `Groups`, `Register(group)`, `SetValue<T>(setting, value)`, `LoadAllAsync()`, `SaveAllAsync()`, `event Action<Setting> OnChanged` |
| `SettingsService` | Class (sealed) | Implementation. Throws on a duplicate `StorageKey`. Skips I/O if `ICloudSaveService` is null |
| `SettingsServiceExtensions` | Static class | `FindSetting(key)`, `ResetToDefaults()` — see [Service extensions](#service-extensions) |
| `SettingsSliderBase` | Abstract MonoBehaviour | `Bind(service, NumericSetting<float>)`, `Bind(service, NumericSetting<int>)`. Base of `SettingsVolumeRow` |
| `SettingsPanel` | Class (`LongLeap.Settings.UI`) | Builds the settings UI in code — see below |
| `SettingsWindowConfig` | ScriptableObject | What the panel shows: sections, rows, captions, font, size |
| `SettingsWindowConfigSet` | Serializable class | One `SettingsWindowConfig` per device profile, with fallback |
| `SettingsVolumeRow` / `SettingsToggleRow` / `SettingsKeyRow` | MonoBehaviour | Code-built rows; created by the panel |
| `SettingKeyAttribute` | PropertyAttribute | Draws a string field as a dropdown of setting keys |

> Set values through `ISettingsService.SetValue`, not `Setting<T>.Value`: only the service clamps the value and fires `OnChanged`. Writing `Value` directly is meant for `Deserialize`.

## Initialization

```csharp
var service = new SettingsService(cloudSave);   // cloudSave may be null: settings live for one session
service.Register(new AudioSettings());
service.Register(new LanguageSettings());
await service.LoadAllAsync();                   // stored values; defaults on missing data or error
```

Then hand `ISettingsService` to whatever needs it (your DI container, the settings window, gameplay code that subscribes to `OnChanged`). Saving is up to the game: typically `await service.SaveAllAsync()` when the settings window closes.

## Usage

### Defining a float slider group

```csharp
public sealed class AudioSettings : ISettingsGroup
{
    public string StorageKey => "settings.audio";
    public string DisplayNameKey => "settings_audio";

    public NumericSetting<float> MasterVolume { get; } = new("audio.master_volume", 1f, 0f, 1f, "settings_master_volume");
    public NumericSetting<float> SfxVolume     { get; } = new("audio.sfx_volume",     1f, 0f, 1f, "settings_sfx_volume");
    public NumericSetting<float> MusicVolume   { get; } = new("audio.music_volume",   1f, 0f, 1f, "settings_music_volume");

    public Setting[] All => new Setting[] { MasterVolume, SfxVolume, MusicVolume };

    public string Serialize() => JsonConvert.SerializeObject(new
    {
        master_volume = MasterVolume.Value,
        sfx_volume    = SfxVolume.Value,
        music_volume  = MusicVolume.Value
    });

    public void Deserialize(string json)
    {
        var data = JsonConvert.DeserializeAnonymousType(json, new
        {
            master_volume = 1f, sfx_volume = 1f, music_volume = 1f
        });
        if (data != null)
        {
            MasterVolume.Value = data.master_volume;
            SfxVolume.Value    = data.sfx_volume;
            MusicVolume.Value  = data.music_volume;
        }
    }
}
```

The samples use Newtonsoft.Json (`com.unity.nuget.newtonsoft-json`); the package itself does not require it.

### Defining an int slider

```csharp
public NumericSetting<int> ResolutionScale { get; } = new("video.resolution_scale", 100, 50, 200, "video_resolution_scale");
```

The slider steps by 1 and is clamped to `[50, 200]`. A `0..1` range is shown as a percentage, other ranges as numbers.

### Defining a bool toggle

```csharp
public Setting<bool> Vibration { get; } = new("gameplay.vibration", true, "gameplay_vibration");
```

### Defining a key binding

```csharp
public Setting<Key> Jump { get; } = new("keys.jump", Key.Space, "settings_key_jump");   // UnityEngine.InputSystem.Key
```

The binding stores the physical key. In the panel, a key already used by another `Setting<Key>` (in any registered group) is swapped with that binding, so no action is left unbound. Esc cancels listening, so Esc itself cannot be bound.

### Defining an enum setting

```csharp
public enum GameLanguage { En, Ru }

public sealed class LanguageSettings : ISettingsGroup
{
    public string StorageKey => "settings.language";
    public string DisplayNameKey => "settings_lang";

    public EnumSetting<GameLanguage> Locale { get; } = new("game.language", GameLanguage.En, "lang");

    public Setting[] All => new Setting[] { Locale };

    public string Serialize() => JsonConvert.SerializeObject(new
    {
        locale = Locale.Value.ToString().ToLowerInvariant()
    });

    public void Deserialize(string json)
    {
        var data = JsonConvert.DeserializeAnonymousType(json, new { locale = "en" });
        if (data != null && Enum.TryParse<GameLanguage>(data.locale, ignoreCase: true, out var p))
            Locale.Value = p;
    }
}
```

Option labels are resolved from `LabelKey` as `{LabelKey}_{enumName.ToLowerInvariant()}`, so the example needs localization keys `lang_en` and `lang_ru`:

```
lang_en  → English
lang_ru  → Русский
```

Option labels, the selected index and applying a selection are available through `IEnumSetting`, without knowing `T`:

```csharp
if (setting is IEnumSetting options)
{
    string[] labels = options.GetOptionLabels();
    int selected = options.GetOptionIndex();
    options.ApplyIndex(newIndex, service);   // goes through service.SetValue
}
```

> `SettingsPanel` has no row for enum settings yet: a config row pointing to an `EnumSetting<T>` logs an error and is skipped.

## Config-driven settings panel

`SettingsPanel` (assembly `LongLeap.Settings.UI`) builds the settings UI **in code** into a `RectTransform` you give it, from a `SettingsWindowConfig` ScriptableObject. It is not tied to a window system: the game's window creates the panel in its root and calls it when opened / closed. `com.longleap.ui` ships such a window (`LongLeap.UI.SettingsWindow`).

```csharp
// in the game's settings window
_panel = new SettingsPanel(root, configSet, onBack: () => Close());
_panel.Bind(settingsService);   // rows appear when the service arrives
_panel.Refresh();               // on open: values may have changed elsewhere
_panel.StopListening();         // on close; saving to storage is the window's job
_panel.Dispose();               // on destroy: unsubscribes from the service
```

`SettingsWindowConfig` (Create ▸ LongLeap ▸ Settings Window Config) holds font, panel size, window captions and a list of **sections**; a section has a title, an optional hint, `HideOnTouch` and **rows**. A row is a `SettingKey` (picked from a dropdown in the inspector) and a fallback `Label`, shown when localization has no translation for the setting's `LabelKey`.

The row view is chosen by the setting's type:

| Setting type | Row |
|---|---|
| `NumericSetting<float>` / `NumericSetting<int>` | `SettingsVolumeRow` — slider |
| `Setting<bool>` | `SettingsToggleRow` — ON/OFF button |
| `Setting<Key>` | `SettingsKeyRow` — key rebind button |

- **Device profile.** The panel takes a `SettingsWindowConfigSet` (Desktop / Mobile / WebDesktop / WebMobile) and picks the config by the global `LongLeap.Tools.DeviceInfo.Profile` (package `com.longleap.tools`). An empty profile falls back to the nearest filled one (WebMobile → Mobile → WebDesktop → Desktop, and so on) with a warning, so one filled config is enough. Sections with `HideOnTouch` are skipped on Mobile and WebMobile.
- **No service.** Until `Bind` is called with a service, the panel shows the config's `UnavailableText`; the back button always works.
- **Validation.** An unknown `SettingKey` or an unsupported setting type logs an error and skips that row; the rest of the panel is built. A section left without rows is hidden.
- **Font.** `Font` from the config; if empty, the TMP default font; if none, the font of any TMP text under the same canvas.
- **Key dropdown.** The `[SettingKey]` drawer lists keys of every `ISettingsGroup` class that has a parameterless constructor. Groups that need constructor arguments are not listed (their keys show as "missing" but still work).
- **Storage.** The panel never saves. Values are applied on every change (so `OnChanged` subscribers react while a slider is dragged); call `SaveAllAsync` when the window closes.

## Service extensions

`SettingsServiceExtensions` works on any `ISettingsService`:

| Method | Purpose |
|---|---|
| `FindSetting(key)` | Find a setting by `Setting.Key` across all groups; `null` if absent |
| `ResetToDefaults()` | Reset every registered setting to its default through `SetValue`, so `OnChanged` fires. New groups are covered automatically |

## UI building blocks

Rows and the panel are assembled from `LongLeap.Tools.UI.RuntimeUi` (package `com.longleap.tools`, assembly `LongLeap.Tools.UI`): text, image, slider, button, layout helpers, plus the shared palette and sizes. The windows of `com.longleap.ui` use the same class, so changing a colour there changes every code-built window.

## Internals

### SetValue flow

```
service.SetValue(s, v)
  → v = s.Coerce(v)              // NumericSetting clamps to [Min,Max]; others identity
  → EqualityComparer check       // no-op if same value
  → s.Value = v
  → OnChanged?.Invoke(s)
```

### Save/Load flow

```
LoadAllAsync()
  → cloud save is null → return
  → foreach group: GetStringAsync(group.StorageKey)
    → !string.IsNullOrEmpty(json) && json != "{}" → group.Deserialize(json)   // sets Value directly, no OnChanged
    → exception → warning, the group keeps its defaults, other groups still load

SaveAllAsync()
  → cloud save is null → return
  → foreach group: group.Serialize() → SetStringAsync(group.StorageKey, json)
  → called by the game (e.g. when the settings window closes), never by the panel
```

First run or missing data → defaults from the setting constructors.
