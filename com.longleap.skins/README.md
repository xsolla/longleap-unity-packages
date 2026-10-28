# com.longleap.skins

Skin catalog model with no Spine dependency.

| Type | Role |
|---|---|
| `SkinId`, `SkinCategoryId` | Typed string ids; `IsNone` for the empty value. |
| `SkinAvailability` | How a player gets a skin: `Locked`, `OwnedFromStart`, `Default`, `Disabled`. |
| `ISkinCatalog` | What exists in the game — categories, skins, titles, icons, group rules. Content, not player state. |
| `SkinCatalog` | In-memory implementation. `Add` is public so tests and tools can build one without assets. |

The catalog says what exists; which skins a given player owns is the host game's inventory.

Namespace: `LongLeap.Skins`. Assembly: `LongLeap.Skins` (no references).

The Spine-backed layout (JSON + ScriptableObject sources) and the layout editor window are in
`com.longleap.skins.spine`.
