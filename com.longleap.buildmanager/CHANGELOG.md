# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The option framework, moved from `com.longleap.core` (was the
  `LongLeap.Core.Editor` assembly and namespace): project options
  (`DefineOption`, `AssetEnumOption`, `CustomOption`), sections, presets,
  build targets, warnings, `ScriptingDefines`, `ProjectWriteContext` and
  `ProjectOptionMenu`. It is now the `LongLeap.BuildManager.Options` assembly
  and namespace, in the `Options/` folder. Behaviour is unchanged. Migration:
  in asmdefs replace the reference `LongLeap.Core.Editor` with
  `LongLeap.BuildManager.Options`; in code replace `using LongLeap.Core.Editor;`
  with `using LongLeap.BuildManager.Options;`; add a dependency on
  `com.longleap.buildmanager`.

### Removed

- The dependency on `com.longleap.core`.

## [0.1.0] - 2026-11-04

First release.

### Added

- **Build Manager window** (**LongLeap / Build Manager**): shows every build
  option registered in the project at once, grouped by section with
  Required / Optional subgroups, per-section actions and a "Before you build"
  warnings block.
- **Staged editing**: clicking a control writes nothing. Changed rows are
  highlighted (accent bar, tint, `*` marker and an `(old -> new)` suffix) and
  the window title gets an asterisk. Setting a value back to the saved one
  clears the highlight. Staged edits survive domain reloads, and closing the
  window with unapplied edits asks to apply or discard them.
- **Apply**: writes all staged edits in one batch (custom writes, then assets,
  then scripting defines), so a whole configuration change costs one define
  write and one domain reload. Options can ask for confirmation; declining one
  drops only that edit. Off-cascades between defines are applied while staging.
- **Revert**: drops the staged edits and re-reads the project.
- **Presets**: one-click staging of a named set of option values, with the
  current preset (or "Custom" / "(modified)") shown in the toolbar. Unknown
  option ids are skipped, so a preset works in projects that lack some
  packages. Ships default GamePix, Xsolla ID and Local presets.
- **Build**: dispatches the highest-priority build target applicable to the
  applied configuration. Disabled, with the reason shown, while edits are
  staged, a recompile is pending, the editor is compiling or in Play Mode, no
  scene is enabled, or a warning blocks the build.
- **WebGL player build target** (`webgl.player`): `BuildPipeline.BuildPlayer`
  into the WebGL output folder (default `Builds/WebGL`), preceded by an Addressables content build when Unity is
  not set to build it with the player. Addressables is reached by reflection,
  so it is not a dependency; if its API cannot be found, the build is
  cancelled rather than shipping stale bundles.
- Pre-build warnings, shown only when the WebGL player target will build: a
  WebGL compression recommendation (Brotli with Decompression Fallback, Info),
  Addressables content that this window will build before the player, and
  build-blocking errors when the Addressables API cannot be found or the output
  folder is inside `Assets/` or at the project root.
- **Project Settings / LongLeap / Build Manager**: the WebGL output folder,
  stored in `ProjectSettings/LongLeapBuildManagerSettings.asset`. Also opened
  from the window's **Settings** toolbar button.
