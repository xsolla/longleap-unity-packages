# LongLeap Localization

Pluggable localization: a static facade over a swappable service, plus a Unity
Localization implementation and a MonoBehaviour for Inspector-bound text.

**Loc**:
Static facade for resolving localized strings. A service is attached at boot via `Loc.Init(service)` and detached via `Loc.Reset(service)`. `Loc.IsReady` tells whether translations are available. Falls back to the key itself when no service is attached.
_Avoid_: i18n, translation

**ILocalizationService**:
The contract behind `Loc`: `Resolve`, `ResolveFormat`, `IsReady`, `OnLanguageChanged`. Games may implement it themselves (e.g. a dictionary of strings).

**LocalizationService**:
The Unity Localization implementation over a `LocalizedStringTable[]`. Caches tables by index, reloads them on locale change via a coroutine host, and keeps the previous locale's strings until the new ones load. No static state; `IDisposable`.

**LocKey**:
A serializable string wrapper for localization keys, used in Inspector fields. Null and empty are the same empty key (`IsEmpty`). Implicit string conversion.

**LocalizedText**:
MonoBehaviour that binds a `LocKey` to a `TMP_Text` or `UI.Text`. Refreshes on `Loc.OnLanguageChanged` and waits while `Loc.IsReady` is false.

## Relationships

- **Localization → Core**: none; the package is standalone.
- **Settings → Localization**: `EnumSetting<T>` and the settings panel rows use `Loc.Resolve` for display labels.
- **UI, XsollaId → Localization**: views subscribe to `Loc.OnLanguageChanged`; the account widget uses `LocalizedText`.
