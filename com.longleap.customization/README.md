# com.longleap.customization

Character customization: what the player owns, what they wear, what colour it is — and the UGUI panel that
drives it. Skeleton-agnostic: the host implements three small interfaces for its own character.

Namespace `LongLeap.Customization` in both assemblies.

## Runtime — `LongLeap.Customization` (needs `LongLeap.Core`, `LongLeap.Skins`)

| Service | Question it answers | Persistence (cloud save key) |
|---|---|---|
| `IInventoryService` / `InventoryService` | What exists (catalog) and what the player has unlocked. | `inventory_skins` |
| `IEquipmentService` / `EquipmentService` | What is worn: one skin per group, many for multi-select groups. Self-repairs to starting outfit. | `equipped_{category}` |
| `IColorService` / `ColorService` | Zone colours and per-skin colours chosen in the palette. | `player_colors` |

Each has a `Null*` implementation (explicit opt-in, never a silent fallback) and a `CloudSave*Persistence`
over `ICloudSaveService`. `SkinDefaultColors` applies a skin's Default Color — one rule for both the panel and
the host's new-player setup.

Wiring order matters: build the catalog (`SkinCatalog`, see `com.longleap.skins.spine`), then
`InventoryService`, then `EquipmentService` (needs inventory), `ColorService`.

## UI — `LongLeap.Customization.UI` (needs `UnityEngine.UI`, `Unity.TextMeshPro`)

| Type | Role |
|---|---|
| `CustomizationPanel` | Category toggles → group rows → variant buttons; palette; equip/unequip; colour. Call `BindCharacter` / `Bind` with the character and `BindPlayer(inventory, equipment, colors)` with the services. |
| `SkinGroupRow`, `SkinVariantView` | Row and button templates the panel clones. `ISkinVariantPreview` is an optional live preview on a variant. |
| `ColorPaletteView`, `HueSliderView` | Round HSV palette, drawn by the shaders below. |
| `NestedScrollRect` | `ScrollRect` that hands cross-axis drags to its parent scroll. |

### Host contracts

The panel never touches a skeleton. The host puts components on its character that implement:

- `ISkinTarget.SetSkins(IReadOnlyList<string>)` — wear exactly this set (required).
- `IColorTarget` — `SetZoneColor`, `SetSkinColor` (optional; no colour button without it).
- `IPreviewPose.Focus(string group)` — turn the preview towards the group being edited (optional).

### Shaders

`LongLeap/Customization/HSVSquare` and `LongLeap/Customization/HueStrip`. Create a material per shader and put
them on the palette's Image and the hue slider's Image. `ColorPaletteView` drives the `_Hue` float of the
HSVSquare material.

## Not in this package

The aggregate "player" object (wallet, run state…), the choice of where the `SkinLayoutSourceSO` asset is
loaded from, tab styling and character-specific pose components are host code.
