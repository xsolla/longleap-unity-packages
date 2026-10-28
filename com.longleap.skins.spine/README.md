# com.longleap.skins.spine

Maps the skin folders of a Spine skeleton onto customization categories, and edits that mapping.

## Host requirements

The host project must provide the Spine runtime with its standard assembly names:
`spine-unity` and `spine-csharp`. Without them this package does not compile.

## Runtime (`LongLeap.Skins.Spine`, namespace `LongLeap.Skins`)

| Type | Role |
|---|---|
| `SkinLayout` | JSON model: categories → groups (Spine skin folders) → per-skin overrides. `BuildCatalog` produces an `ISkinCatalog`. |
| `SkinLayoutSourceSO` | Skeleton + layout JSON + skin icons. Icons live here because JSON cannot hold sprite references. Loading it (Resources, Addressables, a field) is up to the host. |
| `SpineAppearanceConfig` | Colour zones and slot hide rules for the character. |

## Editor (`LongLeap.Skins.Spine.Editor`)

`LongLeap / Skin Layout Editor` — drag folders into categories, set titles, availability, default colour,
colour zones, icons and hide rules. Icons are written to the `SkinLayoutSourceSO` whose Layout field points at
the open JSON.

EditorPrefs keys: `LongLeap.SkinLayout.*`.
