# LongLeap Settings

Typed settings system. Define settings in code as group classes; the settings UI is built in code by `SettingsPanel` from a `SettingsWindowConfig` asset (see [Config-driven settings panel](#config-driven-settings-panel)); values persist to cloud via `ICloudSaveService`.

## Data Sources

Cloud save JSON, one blob per group's `StorageKey`. Example for a float slider group:

```json
// Cloud key: "settings.audio"
{
  "master_volume": 1.0,
  "sfx_volume":    0.8,
  "music_volume":  0.5
}
```

Groups define their own field names via `Serialize()` / `Deserialize()`. Not tied to Setting keys — a group with `Key = "audio.master_volume"` can serialize as `"master_volume"`.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `Setting` | Abstract class | Base: `Key`, `LabelKey`, `ValueType`, `BoxedValue` |
| `Setting<T>` | Class | Typed wrapper: `DefaultValue`, `Value` (public get/set) |
| `NumericSetting<T>` | Class (`where T : struct`) | `Min`, `Max`. Values outside range are clamped via `Coerce` |
| `EnumSetting<T>` | Class (`where T : struct, Enum`) | Auto-generates dropdown labels via `Loc.Resolve($"{LabelKey}_{name.ToLowerInvariant()}")`. Implements `IEnumSetting` (explicit) |
| `IEnumSetting` | Interface | Internal bridge: `GetOptionLabels()`, `GetOptionIndex()`, `ApplyIndex()`. Implemented explicitly by `EnumSetting<T>` |
| `ISettingsGroup` | Interface | `StorageKey`, `DisplayNameKey`, `All`, `Serialize()`, `Deserialize()` |
| `ISettingsService` | Interface | `Groups`, `Register(group)`, `SetValue<T>(setting, value)`, `LoadAllAsync()`, `SaveAllAsync()`, `event Action<Setting> OnChanged` |
| `SettingsService` | Class (sealed) | Implementation. Equality check + `Coerce` before setting. Skips I/O if `ICloudSaveService` is null |
| `SettingsSliderBase` | Abstract class (MonoBehaviour) | `Bind(service, NumericSetting<float>)`, `Bind(service, NumericSetting<int>)`. Base of `SettingsVolumeRow` |
| `SettingsPanel` | Class (`LongLeap.Settings.UI`) | Builds the settings UI in code from a `SettingsWindowConfigSet` — see below |
| `SettingsWindowConfig` / `SettingsWindowConfigSet` | ScriptableObject / serializable set | What the panel shows, per device profile |

## Initialization

### Direct construction

```csharp
var service = new SettingsService(cloudSave);
service.Register(new AudioSettings());
service.Register(new LanguageSettings());
await service.LoadAllAsync();       // loads stored values, falls back to defaults on error/missing
```

### GameBootstrapper wiring

```csharp
_settingsService = new SettingsService(_cloudSaveService);
_settingsService.Register(new AudioSettings());
_settingsService.Register(new LanguageSettings());
var task = _settingsService.LoadAllAsync();
// poll task with timeout in a coroutine, then:
Settings = _settingsService;
```

The service is exposed as `ISettingsService Settings` on the bootstrapper — the game hands it to its settings window, which passes it to `SettingsPanel.Bind(settings)`.

### DI registration

```csharp
container.Bind<ISettingsService>().To<SettingsService>().FromInstance(service).AsSingle();
```

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

### Defining an int slider

```csharp
public NumericSetting<int> ResolutionScale { get; } = new("video.resolution_scale", 100, 50, 200, "video_resolution_scale");
```

Slider steps by 1, clamped to `[50, 200]`.

### Defining a bool toggle

```csharp
public Setting<bool> Vibration { get; } = new("gameplay.vibration", true, "gameplay_vibration");
```

### Defining an enum dropdown

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

Dropdown options are resolved from `LabelKey`: `{LabelKey}_{enumName.ToLowerInvariant()}` → `lang_en`, `lang_ru`.

### Adding a new enum type

No code changes in the package. Define the enum, define the group class, register it in `GameBootstrapper`, add localization keys:

```
lang_en  → English
lang_ru  → Русский
```

Option labels, selection detection and value dispatch are exposed through `IEnumSetting`.

> `SettingsPanel` has no row for enum settings yet: a config row pointing to an `EnumSetting<T>` logs an error and is skipped. Add a dropdown row next to `SettingsToggleRow` when a game needs one.

## Config-driven settings panel

`SettingsPanel` (assembly `LongLeap.Settings.UI`, `Runtime/Panel`) builds the settings UI **in code** into a `RectTransform` you give it, from a `SettingsWindowConfig` ScriptableObject. It is not tied to a window system: the game's window creates the panel in its root and calls it when opened / closed.

```csharp
// in the game's settings window
_panel = new SettingsPanel(root, configSet, onBack: () => Close());
_panel.Bind(settingsService);   // rows appear when the service arrives
_panel.Refresh();               // on open: values may have changed elsewhere
_panel.StopListening();         // on close; saving to storage is the window's job
```

`SettingsWindowConfig` (Create ▸ LongLeap ▸ Settings Window Config) holds font, panel size, window captions and a list of **sections**; a section has a title, an optional hint, `HideOnTouch` and **rows**. A row is a `SettingKey` (picked from a dropdown in the inspector) and a fallback `Label`. The row view is chosen by the setting's type: `NumericSetting<float|int>` → slider, `Setting<bool>` → ON/OFF toggle, `Setting<Key>` → key-rebind button (a key already in use is swapped with the other binding, across all groups).

- **Device profile.** The panel takes a `SettingsWindowConfigSet` (Desktop / Mobile / WebDesktop / WebMobile) and picks the config by the global `LongLeap.Tools.DeviceInfo.Profile` (package `com.longleap.tools`). An empty profile falls back to the nearest filled one (WebMobile → Mobile → …) with a warning, so one filled config is enough.
- **Validation.** An unknown `SettingKey` or an unsupported setting type logs an error and skips that row; the rest of the panel is built. A section left without rows is hidden.
- **Key dropdown.** The `[SettingKey]` drawer lists keys of every `ISettingsGroup` class that has a parameterless constructor. Groups that need constructor arguments are not listed (their keys show as "missing").

## Service extensions

`SettingsServiceExtensions` works on any `ISettingsService` (the interface is unchanged):

| Method | Purpose |
|---|---|
| `FindSetting(key)` | Find a setting by `Setting.Key` across all groups; `null` if absent |
| `ResetToDefaults()` | Reset every registered setting to its default through `SetValue`, so `OnChanged` fires. New groups are covered automatically |

## UI building blocks

Rows and the panel are assembled from `LongLeap.Tools.UI.RuntimeUi` (package `com.longleap.tools`, assembly `LongLeap.Tools.UI`): text, image, slider, button, layout helpers, plus the shared palette and sizes. The same class is used by the windows of `com.longleap.ui`, so changing a colour there changes every code-built window.

## Internals

### SetValue flow

```
service.SetValue(s, v)
  → v = s.Coerce(v)              // NumericSetting clamps to [Min,Max]; others identity
  → EqualityComparer check       // no-op if same value
  → s.Value = v
  → OnChanged?.Invoke(s)
```

### IEnumSetting virtual dispatch

Dropdowns receive a `Setting` reference, cast to `IEnumSetting`, and call the interface methods. `EnumSetting<T>` implements them explicitly — `typeof(T)` resolves at compile time:

```
Bind(Setting s)
  → (s as IEnumSetting).GetOptionLabels()    → Loc.Resolve($"{LabelKey}_{name.ToLowerInvariant()}")
  → (s as IEnumSetting).GetOptionIndex()     → (T[])Enum.GetValues(typeof(T)) index
  → OnChanged → ApplyIndex(i, service)       → service.SetValue(this, values[i])
```

No reflection. `(T[])Enum.GetValues(typeof(T))` is a reference conversion from `Array`.

### Save/Load flow

```
LoadAllAsync()
  → foreach group: GetStringAsync(group.StorageKey)
    → !string.IsNullOrEmpty(json) && json != "{}" → group.Deserialize(json)    // sets Value on each Setting
    → error → warning, keep defaults

SaveAllAsync()
  → foreach group: group.Serialize() → SetStringAsync(key, json)
  → called by SettingsPanel.Hide() (on back button)
  → best-effort Save in OnDestroy() (fire-and-forget)
```

`ICloudSaveService` is nullable — load skips, save no-ops. First-run / missing data → defaults from constructor.
