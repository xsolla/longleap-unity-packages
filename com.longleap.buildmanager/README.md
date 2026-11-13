# LongLeap Build Manager

One editor window that shows every build-relevant option in the project at once,
lets you stage changes without recompiling, applies them in a single batch, and
dispatches the build.

Open it from **LongLeap / Build Manager**.

- Unity 6000.0 or newer. Editor only: nothing from this package goes into a
  player build.
- No package dependencies.
- Two editor assemblies:
  - `LongLeap.BuildManager.Options` (`Options/`): the contract other packages
    implement — option kinds, presets, build targets, warnings, menu helpers.
    Reference this one to declare options.
  - `LongLeap.BuildManager.Editor` (`Editor/`): the window, settings, default
    presets and the WebGL build target. Not auto-referenced.
- Scripting defines are written for **WebGL** only (see [Limits](#limits)).
- Addressables is optional.

## Install (Unity Package Manager)

Unity 6 (6000.0 – 6000.4), WebGL build target. In Unity: **Window → Package Manager → + → Add package from git URL**. This package needs no other LongLeap package.

```
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.buildmanager
```

Pin a version by adding `#v1.0.0` at the end of each URL. Unity packages this one uses are installed automatically. All packages and their dependencies: [root README](../README.md#install-unity-package-manager).

## Why

A menu checkmark that toggles a scripting define writes `PlayerSettings`
immediately and triggers a domain reload. Switching a build between
distributions then costs several clicks with a recompile after each one, and
there is no single place to see the whole configuration or to name a
combination ("this is the portal build"). Build Manager stages the changes,
applies them in one batch, and lets packages declare named presets.

## How it works

- **Staged editing.** Clicking a control writes nothing. Changed rows stay
  highlighted until you press Apply, which performs exactly one
  `SetScriptingDefineSymbols` call and one domain reload for the whole batch.
  Closing the window with staged changes asks to apply or discard them.
- **Defines are the source of truth.** There is no mirror asset to drift.
  Revert re-reads the project.
- **Build needs a clean state.** While anything is staged, while the recompile
  after Apply is pending, or while scripts fail to compile, Build is disabled
  and says why. Building then would compile `#if`-gated code against the old
  defines.
- **Options come from other packages.** Options, presets, warnings and build
  targets arrive from other assemblies through `LongLeap.BuildManager.Options`,
  discovered with `TypeCache`. The window references only that contract, so
  it drops into a project with none of the feature packages and shows no
  options.

## What ships with the package

- **WebGL player build target** (`webgl.player`, priority 0): builds the
  enabled scenes with `BuildPipeline.BuildPlayer` into the WebGL output folder
  (see [Settings](#settings)). Any build target with a higher priority that
  applies to the current configuration replaces it.
- **Addressables prepass**: when Addressables is installed but not set to build
  content with the player, the WebGL target builds the content first. It is
  reached by reflection, so Addressables is not a dependency. If its API cannot
  be found, Build is blocked rather than shipping stale bundles.
- **Pre-build notes** for the WebGL target: Brotli with Decompression Fallback
  is recommended for plain static hosting (Info), and an output folder inside
  `Assets/` or at the project root blocks the build.
- **Default presets**: GamePix, Xsolla ID and Local. They name LongLeap option
  ids; ids no package registered are skipped.

## Settings

**Project Settings / LongLeap / Build Manager** (also the **Settings** button in
the window toolbar):

| Setting | Default | Meaning |
|---|---|---|
| WebGL Output Folder | `Builds/WebGL` | Where the WebGL player target writes the build. Relative to the project folder, or absolute. |

Stored in `ProjectSettings/LongLeapBuildManagerSettings.asset`; commit it so
the whole team builds to the same folder.

## Quick start: declare an option

Implement a provider in an editor assembly that references
`LongLeap.BuildManager.Options`. It is found automatically; nothing has to be
registered.

```csharp
using System.Collections.Generic;
using LongLeap.BuildManager.Options;

namespace MyGame.Editor
{
    internal sealed class MyGameOptions : IProjectOptionSectionProvider
    {
        public const string CheatsId = "mygame.cheats";

        public IEnumerable<ProjectOptionSection> GetSections()
        {
            yield return new ProjectOptionSection(
                "mygame",
                "My Game",
                100,
                new ProjectOption[]
                {
                    new DefineOption(
                        CheatsId,
                        "MYGAME_CHEATS",
                        "Cheats (MYGAME_CHEATS)",
                        "Debug cheat menu. Must be off in release builds.",
                        required: false,
                        order: 10),
                });
        }
    }
}
```

The option appears in the window under **My Game**. To offer the same option as
a menu checkmark that can never disagree with the window:

```csharp
private const string MenuPath = "My Game/Cheats";

[MenuItem(MenuPath, false, 100)]
private static void Toggle() => ProjectOptionMenu.Toggle(MyGameOptions.CheatsId);

[MenuItem(MenuPath, true)]
private static bool ValidateToggle() => ProjectOptionMenu.Validate(MenuPath, MyGameOptions.CheatsId);
```

A menu click writes immediately (one recompile per click); the window stages.

## Extension points

| Interface | Contributes |
|---|---|
| `IProjectOptionSectionProvider` | a block of options (and one-shot actions) |
| `IProjectOptionPresetProvider` | a named set of option values |
| `IProjectWarningProvider` | a pre-build warning, optionally with a Fix button or blocking the build |
| `IProjectBuildTargetProvider` | a way to build, chosen by the current configuration |

Option kinds: `DefineOption` (a scripting define), `AssetEnumOption` (an enum
field in an asset, matched by member **name** so reordering the enum cannot
silently change it), and `CustomOption` (arbitrary read/write, for things like
patching files on disk).

Presets are **exhaustive** by default: every registered option they do not
name is set to its off value. Pass `exhaustive: false` to `ProjectOptionPreset`
for a preset that touches only the options it names.

## Design notes

### Vocabulary

- **Option**: one staged setting. `ProjectOption.Id` is the stable key presets
  address; the label may be rewritten, the id may not.
- **Staged / pending**: changed in the window, not yet written to the project.
  **Saved**: what `PlayerSettings` and the assets say right now.
- **Section**: one package's block of options. Required / Optional inside it
  is derived from `ProjectOption.Required`, never declared.
- **Build target**: a way to build, not a `BuildTarget` platform. The one with
  the highest `Priority` among those applicable to the current configuration
  is used.

### Rules for option providers

- An editor assembly that *enables* a define must never carry that define in
  its own `defineConstraints`, or it disappears exactly when you need it. That
  is why `LongLeap.BuildManager.Options` has empty `references` and `defineConstraints`,
  and why asset fields are reached by string-named `SerializedProperty` rather
  than a typed reference. Feature editor assemblies that declare options
  follow the same rule.
- No `[InitializeOnLoad]` may query `ProjectOptionRegistry`. `TypeCache` is
  complete only after every assembly loads; static registration would make
  sections appear or not depending on compile order.
- Descriptors hold **paths and delegates, never a `UnityEngine.Object`**: the
  registry lives until the next domain reload, and object references would go
  stale.
- Availability checks, action checks and warnings run on focus and after every
  edit, not on every repaint, but they still run often: keep them cheap.

### Invariants

- Every option value is a **string**: `"1"` / `"0"` for checkboxes, the enum
  member **name** for choices. One representation makes pending state
  serializable, dirtiness a single `string.Equals`, and enum storage immune to
  member reordering.
- Apply writes in one order: custom (file) writes, then assets, then defines.
  The compile that the define write triggers must see the already-patched
  files. A write that throws is logged and its change stays staged.
- Reads go through `IProjectReadContext`, never straight to `PlayerSettings`.
  It snapshots the define set once and caches one `SerializedObject` per
  asset, which is what makes it safe to read every option on focus, and why
  reads must not happen during repaint.

### Limits

- `ScriptingDefines.Target` is **WebGL only**. Generalising means threading a
  `NamedBuildTarget` through `DefineOption` and grouping writes per platform in
  `ProjectWriteContext`.
- Addressables is reached by **reflection**, so this package never requires it.
  If the API moves, the build is blocked loudly rather than shipping stale
  bundles.
