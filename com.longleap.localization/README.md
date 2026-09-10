# LongLeap Localization

Wraps `Unity.Localization` with a static facade (`Loc`), typed key wrapper, and component-based text binding.

## Data Sources

Localization data lives in Unity's Localization package assets, backed by Addressables.

| Asset | Location | Purpose |
|---|---|---|
| `LocalizationSettings` | `Assets/Localization/LocalizationSettings.asset` | Root settings — available locales, locale selector |
| `StringTableCollection` | `Assets/Localization/{Name}.asset` | Shared key definitions across all locales |
| `StringTable` (per locale) | `Assets/Localization/{Name}_{locale}.asset` | Actual translations for a specific locale |
| `Locale` | `Assets/Localization/{code}.asset` | Locale definitions (`en`, `ru`, etc.) |

The package uses `LocalizedStringTable` objects — Inspector-assigned handles that know their `TableCollectionName` and load the correct per-locale `StringTable` via Addressables at runtime. Tables are grouped under `Assets/AddressableAssetsData/` in groups like `Localization-String-Tables-en`.

Keys are arbitrary strings defined in the shared table data. No key format is enforced by the package.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `Loc` | Static class | Convenience facade: `Resolve(key)` returns translated string, `ResolveFormat(key, args)` returns formatted string. Falls back to `string.Empty` for null keys; falls back to the raw key string when service is unavailable. |
| `ILocalizationService` | Interface | `Resolve(key)`, `ResolveFormat(key, args)` |
| `LocalizationService` | Class | Manages `StringTable` cache, locale switching via `SwitchLanguage(code)`, automatic table reload on locale change. Instance `OnLanguageChanged` event. Constructor: `(LocalizedStringTable[] localizedTables, Dictionary<string, StringTable> preloaded = null)`. Constructor fires `OnLanguageChanged` immediately. No static state — use `Loc.Init(service)` to wire the static facade. |
| `LocKey` | Struct, `[Serializable]` | Typed key wrapper. Implicit conversions to/from `string`. `IsEmpty`, value equality, Inspector-serializable. Static `Format(string format, params object[] args)` creates a `LocKey` from a format string. |
| `LocalizedText` | MonoBehaviour | `[DisallowMultipleComponent]`. Binds a `LocKey` to `TMP_Text` (preferred) or `UnityEngine.UI.Text` (fallback). Subscribes to `OnLanguageChanged` for auto-refresh. `SetKey(key)`, `SetFormatArgs(args)`. |

## Initialization

This package does **not** provide a bootstrapper component. You must write your own initialization code following this pattern:

1. Collect the required `LocalizedStringTable[]` references (e.g. via Inspector-assigned fields on a MonoBehaviour, or via Addressables).
2. Construct a `LocalizationService(localizedTables, preloaded)`.
3. Call `Loc.Init(service)` to wire the static facade.
4. Call `SetCoroutineHost(someMonoBehaviour)` to enable locale switching (table reload uses coroutines).
4. Wait for tables to load before using localization.

### Preloading pattern (recommended)

```csharp
public class MyBootstrapper : MonoBehaviour
{
    [SerializeField] private LocalizedStringTable[] _tables;

    public bool IsReady { get; private set; }
    public LocalizationService Service { get; private set; }

    IEnumerator Start()
    {
        var cache = new Dictionary<string, StringTable>();
        yield return LocalizationService.LoadTables(_tables, cache);
        Service = new LocalizationService(_tables, cache);
        Service.SetCoroutineHost(this);
        Loc.Init(Service);
        IsReady = true;
    }
}
```

Consumer wait pattern:

```csharp
IEnumerator Init(MyBootstrapper bootstrapper)
{
    while (!bootstrapper.IsReady)
        yield return null;
}
```

### Manual path (no preloading)

```csharp
var tables = new LocalizedStringTable[] { myTableReference };
var service = new LocalizationService(tables);
service.SetCoroutineHost(someMonoBehaviour); // required for locale switching
Loc.Init(service);
```

If you skip `SetCoroutineHost`, locale switching will clear the cache but never reload tables — all `Resolve()` calls will log warnings and return raw keys.

### Pure-code path (no MonoBehaviour at all)

If you don't need runtime locale switching, you can construct `LocalizationService` without a coroutine host. Just ensure all tables are pre-visited (Addressables cache) before the locale changes, or never change locale after init. Call `Loc.Init(service)` to enable the static facade for lookups.

### DI registration

```csharp
var service = new LocalizationService(tables);
Loc.Init(service);
container.Bind<ILocalizationService>().To<LocalizationService>().FromInstance(service).AsSingle();
```

## Usage

### Resolving keys

```csharp
string text  = Loc.Resolve("welcome_message");       // plain lookup
string label = Loc.ResolveFormat("player_level", 5);  // string.Format after resolve
```

**Null key behaviour**: `Loc.Resolve(null)` returns `string.Empty` — safe to call with nullable keys. When the service is not yet initialized (null via `Loc.Init`), the facade returns `string.Empty` for null and the raw key string for non-null keys.

### Component binding

Attach `LocalizedText` to any GameObject with a `TMP_Text` or `UnityEngine.UI.Text` component. Set the `Key` field in the Inspector. The component auto-refreshes when locale changes. Only one `LocalizedText` per GameObject (`[DisallowMultipleComponent]`).

In code:
```csharp
GetComponent<LocalizedText>().SetKey("score_display");
GetComponent<LocalizedText>().SetFormatArgs(current, max);
```

`TMP_Text` takes priority over `UnityEngine.UI.Text` if both are present.

### Locale switching

```csharp
service.SwitchLanguage("ru");
```

This finds the matching `Locale` in `AvailableLocales` and assigns it to `LocalizationSettings.SelectedLocale`, which triggers:
1. `OnUnityLocaleChanged` — table cache is cleared
2. All `LocalizedStringTable`s are reloaded for the new locale (via coroutine on the host)
3. `OnLanguageChanged` event fires — all `LocalizedText` components refresh

### Key not found

If no table contains the requested key, a warning is logged (`[Localization] Missing translation for key '...'`) and the raw key string is returned as fallback. This means incomplete tables are visible rather than silent.

### Table lookup order

`Resolve()` iterates the `_localizedTables` array **in order**. The first table with the key wins. No lazy loading — if a table isn't in the cache, the key is simply not found.

### LocKey.Format

`LocKey.Format` is a **static** method that creates a `LocKey` from a format string — useful for enum-based key generation:

```csharp
var key = LocKey.Format("enemy_{0}_name", enemyId);  // → "enemy_goblin_name"
string text = Loc.Resolve(key);
```

## Internals

### Initialization

The static facade `Loc` is initialized once at boot via `Loc.Init(service)`. The service itself is constructed through normal DI and has no static state. The `Loc` facade forwards `OnLanguageChanged` from the service to static subscribers.

### Table cache

A `Dictionary<string, StringTable>` keyed by `TableCollectionName`. Populated on construction (via `preloaded` dict) or on locale switch (via `ReloadTablesAfterLocaleChange`). No lazy loading — cache miss means key not found.

### Constructor event

The `LocalizationService` constructor fires `OnLanguageChanged?.Invoke()` immediately after subscribing to `SelectedLocaleChanged`. This ensures subscribers (like `LocalizedText` components) receive the initial locale right away, without waiting for a locale switch.

### Locale change handling

`SwitchLanguage()` sets `LocalizationSettings.SelectedLocale`, which raises `SelectedLocaleChanged`. The handler `OnUnityLocaleChanged` then:

1. Clears the table cache.
2. If a `_coroutineHost` is set, starts `ReloadTablesAfterLocaleChange()` coroutine — loads all tables for the new locale, then fires `OnLanguageChanged`.
3. If no coroutine host is set, fires `OnLanguageChanged` immediately (with an empty cache — all `Resolve()` calls will warn and return raw keys).

There is no `_isSwitching` guard. The handler reacts to every `SelectedLocaleChanged` event, whether triggered programmatically via `SwitchLanguage()` or externally (e.g. system locale change).

### Domain reload safety

With `Loc.Init(service)`, the static facade reference and `OnLanguageChanged` subscribers are reset each time the application starts. When "Enter Play Mode Options → Disable Domain Reload" is enabled, ensure `Loc.Init()` is called again on each Play session entry — the static state persists across domain reloads when that option is active. `LocalizationService` itself has no static state and does not require special cleanup.
