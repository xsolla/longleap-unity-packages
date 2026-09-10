# LongLeap Localization

Localization service using Unity's Localization package. Static facade + MonoBehaviour component for Inspector-bound text.

**Loc**:
Static facade for resolving localized strings. Initialized once at boot via `Loc.Init(service)`. `Loc.Resolve(key)` returns the string in the current language (EN/RU). Falls back to the key itself if the service is unavailable.
_Avoid_: i18n, translation

**LocKey**:
A serializable string wrapper for localization keys. Used in Inspector fields throughout config assets. Has `IsEmpty` and implicit string conversion.

**LocalizationService**:
Wraps Unity's `LocalizedStringDatabase`. Handles language switching and fallback logic. No static state — constructed via DI, wired to `Loc` via `Loc.Init()`.

**LocalizedText**:
MonoBehaviour component that binds a `LocKey` to a `TMP_Text`. Auto-updates on language change via `Loc.OnLanguageChanged`.

## Relationships

- **Localization → Core**: `LocKey` is a standalone newtype; no core dependency.
- **Settings → Localization**: `EnumSetting<T>` and `SettingsPanel` use `Loc.Resolve` for display labels.
