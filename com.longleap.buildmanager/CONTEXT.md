# Build Manager — context

## Vocabulary

**Option** — one staged setting. `ProjectOption.Id` is the stable key presets
address; the label may be rewritten, the id may not.
_Avoid_: setting, flag, toggle (all three already mean something else here).

**Staged / pending** — changed in the window, not yet written to the project.
**Saved** — what `PlayerSettings` and the assets say right now.
_Avoid_: dirty (used for the *visual* state), draft.

**Section** — one package's block of options. Required/Optional inside it is
derived from `ProjectOption.Required`, never declared.

**Build target** — a way to build, not a `BuildTarget` platform. Selected by
highest `Priority` among those applicable to the current configuration.
_Avoid_: platform, builder.

## Invariants

- Every option value is a **string**: `"1"`/`"0"` for checkboxes, the enum
  member **name** for choices. One representation makes pending state
  serializable, dirtiness a single `string.Equals`, and enum storage immune to
  member reordering.
- `LongLeap.Core.Editor` keeps `references: []` and `defineConstraints: []`
  forever. So do the feature editor assemblies that reference it.
- No `[InitializeOnLoad]` may query `ProjectOptionRegistry`. `TypeCache` is
  populated only after every assembly loads; static registration would make
  project-side sections appear or not depending on compile order.
- Descriptors hold **paths and delegates, never a `UnityEngine.Object`** — the
  registry lives until the next domain reload and would otherwise go stale.
- Apply writes in one order: custom/file writes, then assets, then defines. The
  compile that the define write triggers must see already-corrected files.
- Reads go through `IProjectReadContext`, never straight to `PlayerSettings`.
  It snapshots the define set once and caches one `SerializedObject` per asset,
  which is what makes it safe to read every option on focus — and why reads
  must not happen during repaint.

## Deliberate limits

- `ScriptingDefines.Target` is **WebGL only**, matching every line of this
  project. Generalising means threading a `NamedBuildTarget` through
  `DefineOption` and grouping writes per platform in `ProjectWriteContext`.
- Addressables is reached by **reflection**, so this package never requires it.
  If the API moves, the prepass aborts the build loudly rather than shipping
  stale bundles.
- The GamePix vendor build entry point is **discovered, not named**: its menu
  item is preferred over an internal type name, because menu paths change less.
