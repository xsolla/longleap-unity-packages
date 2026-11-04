# LongLeap Localization

Pluggable localization for Unity 6:

- `Loc` — a static facade that any code can call to resolve keys.
- `ILocalizationService` — the contract behind it. Plug in your own implementation,
  or use the bundled one.
- `LocalizationService` — an implementation backed by Unity Localization string
  tables, with locale switching that never flashes raw keys.
- `LocKey` — a serializable key type for Inspector fields.
- `LocalizedText` — a component that keeps a `TMP_Text` / `UI.Text` translated.

## Installation

Add the package to `Packages/manifest.json`. Dependencies: `com.unity.localization`
1.5.0 and `com.unity.ugui` 2.0.0 (TextMeshPro ships inside uGUI in Unity 6).

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `Loc` | Static class | `Init(service)`, `Reset(service)`, `IsReady`, `Resolve(key)`, `ResolveFormat(key, args)`, static `OnLanguageChanged`. Without a service returns keys as-is (`string.Empty` for null). |
| `ILocalizationService` | Interface | `Resolve`, `ResolveFormat`, `IsReady`, `OnLanguageChanged`. |
| `LocalizationService` | Class, `IDisposable` | Unity Localization implementation. Constructor `(LocalizedStringTable[] tables, StringTable[] preloaded = null)`. `SetCoroutineHost(host)`, `SwitchLanguage(code) → bool`, `CurrentLocaleCode`, `GetAvailableLocaleCodes()`, static `LoadTables(tables, target)` coroutine, `Dispose()`. |
| `LocKey` | `[Serializable]` struct | Key wrapper. Implicit conversions to and from `string`, `IsEmpty`, value equality, `LocKey.Format(format, args)`. Null and `""` are the same empty key. |
| `LocalizedText` | MonoBehaviour | Binds a `LocKey` to `TMP_Text` (preferred) or `UnityEngine.UI.Text`. `Key`, `SetKey(key)`, `SetFormatArgs(args)`. One per GameObject. |

## Setup with Unity Localization

Localization data lives in Unity Localization assets (Addressables-backed):
`LocalizationSettings`, `Locale` assets, and `StringTableCollection`s with one
`StringTable` per locale. Keys are arbitrary strings; the package enforces no format.

The package has no bootstrapper. Write one along these lines:

1. Reference the `LocalizedStringTable`s to search (e.g. Inspector fields).
2. Load them with `LocalizationService.LoadTables` before creating the service.
3. Create the service, give it a coroutine host, and attach it with `Loc.Init`.
4. On teardown, call `Loc.Reset(service)` and `service.Dispose()`.

```csharp
public class LocalizationBootstrapper : MonoBehaviour
{
    [SerializeField] private LocalizedStringTable[] _tables;

    public LocalizationService Service { get; private set; }

    IEnumerator Start()
    {
        yield return LocalizationSettings.InitializationOperation;

        var preloaded = new StringTable[_tables.Length];
        yield return LocalizationService.LoadTables(_tables, preloaded);

        Service = new LocalizationService(_tables, preloaded);
        Service.SetCoroutineHost(this); // runs table reloads on locale change
        Loc.Init(Service);
    }

    void OnDestroy()
    {
        if (Service == null)
            return;

        Loc.Reset(Service);
        Service.Dispose();
    }
}
```

Wait for `Loc.IsReady` before showing UI that must not display raw keys.
`LocalizedText` already handles this on its own.

**Coroutine host.** Table reloads on locale change run as coroutines on the host.
Without an active host, a locale change drops all tables (logged as a warning), and
texts fall back to keys until the next successful reload.

## Using your own string source

`Loc` accepts any `ILocalizationService`. A game without Unity Localization tables
can attach a dictionary-backed service:

```csharp
public sealed class EnglishStrings : ILocalizationService
{
    static readonly Dictionary<string, string> Strings = new() { ["btn_back"] = "BACK" };

    public event Action OnLanguageChanged { add { } remove { } }
    public bool IsReady => true;

    public string Resolve(string key) =>
        string.IsNullOrEmpty(key) ? string.Empty
        : Strings.TryGetValue(key, out var v) ? v : key;

    public string ResolveFormat(string key, params object[] args)
    {
        try { return string.Format(Resolve(key), args); }
        catch (FormatException) { return Resolve(key); }
    }
}

Loc.Init(new EnglishStrings());
```

Implementations should follow the same contract: return the key when it is
missing, return `string.Empty` for null, and never throw from `ResolveFormat`.

## Usage

### Resolving keys

```csharp
string text  = Loc.Resolve("welcome_message");
string label = Loc.ResolveFormat("player_level", 5); // "Level {0}" → "Level 5"
```

Formatting uses `string.Format` placeholders (`{0}`). Unity Smart Strings
arguments are not applied. A malformed format string returns the unformatted text
and logs a warning instead of throwing.

To refresh custom views on locale change, subscribe to `Loc.OnLanguageChanged` in
`OnEnable` and unsubscribe in `OnDisable`. It also fires on every `Loc.Init`.

### LocalizedText

Add `LocalizedText` next to a `TMP_Text` or `UI.Text` and set **Key** in the
Inspector. While `Loc.IsReady` is false, the text authored in the prefab is kept.

```csharp
var label = GetComponent<LocalizedText>();
label.SetKey("score_display");
label.SetFormatArgs(current, max);
label.SetKey(null); // clears the text
```

### Locale switching

```csharp
if (service.SwitchLanguage("ru"))
{
    // Reload started: Loc.IsReady is false until the new tables are in,
    // then OnLanguageChanged fires.
}
```

`SwitchLanguage` returns false without reloading when the code is empty, the
locale is not available (a warning is logged), or the locale is already selected.
Locale changes made outside the service, e.g. from the Unity Inspector, are picked
up too.

During a reload, `Resolve` keeps returning the previous locale's strings, so raw
keys never flash on screen. If the locale is switched again mid-reload, only the
latest reload applies.

### Keys

```csharp
[SerializeField] private LocKey _title;

var key = LocKey.Format("enemy_{0}_name", enemyId); // "enemy_goblin_name"
string text = Loc.Resolve(key);
```

## Behaviour notes

- **Lookup order.** Tables are searched in constructor order. The first non-empty
  entry wins. Only loaded tables are searched (no lazy loading).
- **Missing keys.** The key itself is returned, and
  `[Localization] Missing translation for key '…'` is logged once per key until the
  tables change.
- **Failed table loads** are logged as errors with the table index and GUID.
- **Tables are cached by index, not by name.** At runtime in a player build,
  `TableReference.TableCollectionName` can be null for GUID-based references until
  the shared table data has loaded.
- **Host destroyed mid-reload.** If the coroutine host is destroyed or deactivated
  during a reload and `Dispose` was not called, the reload is abandoned. `IsReady`
  becomes true again, and the previous locale's strings stay in use.
- **Domain reload disabled.** `Loc` clears its static state when Play Mode starts.
- **No LocalizationSettings.** The service can still be created and resolves keys
  as-is. `SwitchLanguage` logs a warning and returns false.
