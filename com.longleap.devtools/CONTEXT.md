# CONTEXT — com.longleap.devtools

In-build debug tooling. Home for on-screen diagnostic widgets; currently one.

## Terms

- **DevTools** — this package. Runtime code exists **only** when the `LONGLEAP_DEBUG`
  scripting define is set: `Runtime/LongLeap.DevTools.asmdef` carries
  `defineConstraints: ["LONGLEAP_DEBUG"]`, so without the define the assembly is not built
  at all and nothing can reference it.
- **CloudSaveDebugPanel** — IMGUI overlay listing the cloud save contents. Spawns itself from
  code (`Spawn(Func<ICloudSaveService>, string writeScope)`), `DontDestroyOnLoad`, right-hand
  side of the screen, collapsed by default. Knows nothing about any particular game: the game
  supplies the service through a delegate.
- **Service cache** — what the panel shows. It reads `ICloudSaveBulkAccess.SnapshotAsync()`,
  which is the service's in-memory state, not a fresh read of the backend. A key that nothing
  read or wrote this session is absent. There is no way to do better: neither `IPlayerPrefs`
  nor Unity's `PlayerPrefs` can enumerate keys.
- **Write scope** — human-readable label for how far a delete travels, passed in by the game.
  Under `GAMEPIX` the provider is a no-op so a delete is local only; on a Firebase build the
  key is also removed from `cloud_saves/{uid}`.
- **CloudSaveDebugView** — the pure, `UnityEngine`-free half: grouping, filtering, timestamp
  and value formatting. All logic lives here so it can be unit-tested; the MonoBehaviour only
  draws. Groups are derived generically from the text before the first `_`, never from a list
  of known game keys.

## Editor

`LongLeap / DevTools / Cloud Save Panel (LONGLEAP_DEBUG)` toggles the define (WebGL target only).
`Editor/LongLeap.DevTools.Editor.asmdef` deliberately has **no** `defineConstraints` — a menu
that enables `LONGLEAP_DEBUG` cannot itself require it.

## Game-side wiring

`Assets/Scripts/DevTools/CloudSaveDebugPanelInstaller.cs` — the only project-specific part:
where this game keeps its cloud save service, and the write-scope label.
