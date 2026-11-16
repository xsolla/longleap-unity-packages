# LongLeap Build Manager

One editor window that stages every build-relevant option, applies them in one batch and dispatches the build. Editor only. See README.md for usage and the extension points.

**Option** (`ProjectOption`):
One staged setting. `Id` is the stable key presets address — the label may be rewritten, the id may not. Kinds: `DefineOption` (a scripting define), `AssetEnumOption` (an enum field in an asset, matched by member **name**), `CustomOption` (arbitrary read/write, e.g. patching files on disk). Every value is a **string**: `"1"` / `"0"` for checkboxes, the enum member name for choices. Avoid: *Setting*, *Flag*, *Toggle*.

**Section** (`ProjectOptionSection`, `IProjectOptionSectionProvider`):
One package's block of options (and one-shot actions). Required / Optional inside it is derived from `ProjectOption.Required`, never declared separately.

**Staged / pending vs saved**:
Staged — changed in the window, not yet written. Saved — what `PlayerSettings` and the assets say right now. Defines are the source of truth; there is no mirror asset. **Apply** writes all staged changes in one batch: custom (file) writes, then assets, then defines, so the one recompile sees the patched files. **Revert** re-reads the project.

**Preset** (`ProjectOptionPreset`, `IProjectOptionPresetProvider`):
A named set of option values ("GamePix", "Xsolla ID", "Local"). **Exhaustive** by default: every registered option it does not name goes to its off value; `exhaustive: false` touches only the named ones. Ids no package registered are skipped.

**Warning** (`IProjectWarningProvider`):
A pre-build note: info, a warning with an optional **Fix** button, or a build-blocking error.

**Build target** (`IProjectBuildTargetProvider`):
A *way to build*, not a Unity `BuildTarget` platform. Among those applicable to the current configuration, the one with the highest `Priority` is used. The package ships `webgl.player` (priority 0); GamePix replaces it with its own.

**Read / write context** (`IProjectReadContext`, `IProjectWriteContext`):
All reads go through a snapshot (one define set, one cached `SerializedObject` per asset), never straight to `PlayerSettings`. Writers request `RequestAssetRefresh` instead of refreshing themselves, so one Apply does one refresh.

**Menu mirror** (`ProjectOptionMenu.Toggle` / `.Validate`):
A menu checkmark that reads and writes the same option as the window, so the two can never disagree. A menu click writes immediately; the window stages.

## Invariants

- `LongLeap.BuildManager.Options` has empty `references` and `defineConstraints`. An editor assembly that *enables* a define must never carry that define in its own `defineConstraints`.
- No `[InitializeOnLoad]` may query `ProjectOptionRegistry`: `TypeCache` is complete only after every assembly loads.
- Descriptors hold paths and delegates, never a `UnityEngine.Object` (the registry outlives object references until the next domain reload).
- Build is disabled while anything is staged, a recompile is pending, or scripts fail to compile.
- Defines are written for the **WebGL** target only.
- Addressables is reached by reflection; if its API cannot be found, the build is blocked rather than shipping stale bundles.

## Relationships

- **Feature packages → Build Manager**: gamepix, xsollaid, backpack (and devtools) declare their options, presets, warnings and targets against `LongLeap.BuildManager.Options`. The window never references them back, so it works in a project with none of them.
