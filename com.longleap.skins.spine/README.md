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

Slots of colour zones and hide rules are picked in `SlotPickerPopup`, opened by the **Выбрать…** button of a
`Slots` list (in the `SpineAppearanceConfig` inspector and in the config panel of the Skin Layout Editor) and by
the **Прячет** button of a skin row. The popup has a search box (space-separated terms, matched against slot
and bone names), slots grouped by bone, a filter by the slots a skin folder uses, and checkboxes that stay open
for several picks. Every change is written at once, with Undo. Slots that are no longer in the skeleton are
shown in red, so they can be removed.

EditorPrefs keys: `LongLeap.SkinLayout.*`.
