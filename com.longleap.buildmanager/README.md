# LongLeap Build Manager

One editor window that shows every build-relevant option in the project at once,
lets you stage changes without recompiling, applies them in a single batch, and
dispatches the build.

Open it from **LongLeap / Build Manager**.

## Why

Before this, each option was an individual `[MenuItem]` checkmark. Every click
wrote `PlayerSettings` scripting defines immediately and triggered a domain
reload, so switching distribution meant half a dozen clicks with a recompile
between each one — and there was no way to see the whole configuration at once,
or to say "this combination is the portal build".

## How it works

- **Staged editing.** Clicking a control writes nothing. Changed rows stay
  highlighted until you press Apply, which performs exactly one
  `SetScriptingDefineSymbols` call and one domain reload for the whole batch.
- **Defines are the source of truth.** There is no mirror asset to drift.
  Revert re-reads the project.
- **Build needs a clean state.** While anything is staged — or while the
  recompile after Apply is still pending — Build is disabled and says why.
  Building in that window would compile `#if`-gated code against the old
  defines.
- **Nothing is hardcoded here.** Options, presets, warnings and build targets
  all arrive from other assemblies through `LongLeap.Core.Editor`, discovered
  with `TypeCache`. This package references only that contract, so it drops
  into a project with none of the feature packages and simply shows nothing.

## Declaring options from your own package

Implement any of these in an editor assembly that references
`LongLeap.Core.Editor`:

| Interface | Contributes |
|---|---|
| `IProjectOptionSectionProvider` | a block of options (and one-shot actions) |
| `IProjectOptionPresetProvider` | a named set of option values |
| `IProjectWarningProvider` | a pre-build warning, optionally with a Fix button |
| `IProjectBuildTargetProvider` | a way to build, chosen by current configuration |

Option kinds: `DefineOption` (a scripting define), `AssetEnumOption` (an enum
field in an asset, matched by member **name** so reordering the enum cannot
silently change it), and `CustomOption` (arbitrary read/write, for things like
patching files on disk).

Render the same option from a menu item with `ProjectOptionMenu.Toggle` /
`.Select` / `.Validate`, so the menu and the window can never disagree.

## Rule that must not be broken

An editor assembly that *enables* a define must never carry that define in its
own `defineConstraints`, or it disappears exactly when you need it. That is why
`LongLeap.Core.Editor` has empty `references` and empty `defineConstraints`,
and why asset fields are reached by string-named `SerializedProperty` rather
than a typed reference.
