# LongLeap Achievements

Stats-driven achievement system with cascading unlocks, typed IDs, and configurable providers. Achievements are defined by the game (via `IAchievementProvider`); progress is derived from `IStatSystem` at runtime; unlock state is persisted through `IAchievementStateStore`.

## Install (Unity Package Manager)

Unity 6 (6000.0 – 6000.4), WebGL build target. In Unity: **Window → Package Manager → + → Add package from git URL**. Add the packages in this order: a package installed from Git does not pull in other Git packages, so the LongLeap packages it needs go first.

```
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.core
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.stats
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.achievements
```

Pin a version by adding `#v1.0.0` at the end of each URL. Unity packages this one uses are installed automatically. All packages and their dependencies: [root README](../README.md#install-unity-package-manager).

## Data Sources

### Achievement definitions

Supplied by `IAchievementProvider.LoadAsync()`; the package ships JSON providers (see [Loading definitions from JSON](#loading-definitions-from-json)). Each objective is either **stat-driven** (`statPath` + `statTarget`) or **dependency-based** (`required` — an array of `AchievementId`s that must unlock first). The two modes are mutually exclusive.

```json
{
  "id": "kill_100",
  "group": "combat",
  "isHidden": false,
  "objectives": [
    { "id": "kc_100", "statPath": "stats.enemies_killed", "statTarget": 100 }
  ]
}
```

### Unlock state

Persisted via `IAchievementStateStore`. Only the unlocked flag and timestamp are saved — objective progress is derived live from `IStatSystem`.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `AchievementId` | Readonly struct | Typed ID wrapper. `None` sentinel, `IsNone`, value equality |
| `GroupId` | Readonly struct | Typed group wrapper. `None` sentinel, implicit from `string` |
| `ObjectiveId` | Readonly struct | Typed objective ID wrapper, value equality |
| `Progress` | Readonly struct | `Current` (int), `Target` (int), `Ratio` (float), `IsComplete` (bool) |
| `Objective` | Sealed class | One objective: `Id`, `StatPath`/`StatTarget` or `Required` (AchievementId[]) |
| `AchievementDefinition` | Sealed class | Full data: `Id`, `Group`, `IsHidden`, `Prerequisites`, `Objectives` |
| `ObjectiveProgress` | Sealed class | Runtime progress: `ObjectiveId`, `Progress` |
| `AchievementStatus` | Sealed class | Full status: `Definition`, `IsAvailable`, `IsUnlocked`, `UnlockedAt` (`DateTimeOffset?`), `ObjectiveProgresses` |
| `AchievementStateEntry` | Sealed class, `[Serializable]` | `AchievementId` (string), `IsUnlocked`, `UnlockedTimestamp` |
| `AchievementStatesSnapshot` | Sealed class, `[Serializable]` | `Version` (int), `Entries` |
| `IAchievementSystem` | Interface | `Initialize(provider, stateStore, gameBridge = null, stats = null)`, `Shutdown()`, `GetAll()`, `GetByGroup(GroupId)`, `GetAvailable()`, `GetAchieved()`, `GetPartiallyAchieved()`, `GetStatus(AchievementId)`, `GetGroups()`, `ForceUnlock(AchievementId)`, `ResetAll()` |
| `AchievementSystem` | Sealed class | Implementation. Stat subscription, unlock cascading, state reconciliation |
| `IAchievementProvider` | Interface | `Task<AchievementDefinition[]> LoadAsync()`. Use a provider below or implement your own |
| `ResourcesAchievementProvider` | Sealed class `: IAchievementProvider` | Loads `manifest.json` + listed files from `Resources/<folder>/` (default `Achievements`). Main thread only |
| `JsonAchievementProvider` | Sealed class `: IAchievementProvider` | Same JSON format from any text source: `Func<string, string>` (file name → text, `null` if missing) |
| `IAchievementGameBridge` | Interface | `OnAvailable(AchievementId)`, `OnUnlocked(AchievementDefinition)`. Optional |
| `IAchievementStateStore` | Interface | `LoadAsync()`, `MarkUnlockedAsync()`, `DeleteAllAsync()`. Game implements. `DeleteAllAsync` is expected to delete achievement state only |
| `AchievementValidator` | Static class | `Validate(AchievementDefinition[])` -> `ValidationResult` |
| `ValidationResult` | Sealed class | `IsValid` (bool), `Errors` (string[]), `Warnings` (string[]) |

## Initialization

```csharp
var system = new AchievementSystem();
await system.Initialize(
    provider: myAchievementProvider,
    stateStore: myStateStore,
    gameBridge: myBridge,   // optional
    stats: myStatSystem     // optional
);
```

After initialization, definitions are loaded, validated and normalized, saved unlock state is reconciled, and if `stats` is provided, subscriptions are created for all `statPath` values. `Shutdown()` removes only the system's own stat subscriptions — `IStatSystem` is shared with other systems.

## Usage

### Loading definitions from JSON

Put the files in a `Resources/Achievements/` folder: a `manifest.json` that lists the files, and the files themselves.

```json
// manifest.json — file order sets definition order (and card order in the collection)
{ "files": ["combat.json", "heroes.json"] }

// combat.json
{ "achievements": [ { "id": "kill_100", "group": "combat", "objectives": [ { "id": "kc_100", "statPath": "kill", "statTarget": 100 } ] } ] }
```

```csharp
IAchievementProvider provider = new ResourcesAchievementProvider();          // Resources/Achievements/
IAchievementProvider custom   = new ResourcesAchievementProvider("Data/Ach"); // Resources/Data/Ach/
IAchievementProvider fromText = new JsonAchievementProvider(fileName => LoadText(fileName));
```

The files ship inside the build, so loading makes no HTTP requests and works on portals that rehost the build (e.g. GamePix). `LoadAsync` completes synchronously.

Error handling: a missing or unparsable `manifest.json`, or a listed file that is missing, is logged as an **error**. An empty definition set silently ungates everything gated by achievements, so treat these errors as a broken build. A file that fails to parse is logged as a warning and skipped; the other files still load.

### Querying achievements

```csharp
var allGroups = system.GetGroups();
foreach (var group in allGroups)
{
    var achievements = system.GetByGroup(group);
    foreach (var status in achievements)
    {
        foreach (var op in status.ObjectiveProgresses)
            Debug.Log($"{op.ObjectiveId}: {op.Progress.Current}/{op.Progress.Target}");
    }
}
```

### Game bridge

```csharp
public class AchievementNotifier : IAchievementGameBridge
{
    public void OnAvailable(AchievementId id) { /* show hint */ }
    public void OnUnlocked(AchievementDefinition def) { /* show popup, grant reward */ }
}
```

### Force-unlock for debugging

```csharp
system.ForceUnlock(new AchievementId("kill_100"));
// Triggers cascade — dependents re-evaluated
```

## Internals

### Stat-driven progress

When `IStatSystem` is passed to `Initialize()`, the system subscribes to stat changes for every `statPath`. Progress is recomputed from scratch each time — if a stat value drops, the shown progress drops too. Unlocks are permanent.

### Cascading unlocks

An achievement unlocks once all its prerequisites are unlocked and every objective is complete — stat-driven and dependency-based objectives alike. When an achievement unlocks, all achievements depending on it (via `Prerequisites` or `Required`) are re-evaluated, so a chain A -> B -> C drains fully in one call regardless of definition order.

### Hidden achievements

`IsHidden = true` excludes from `GetByGroup`, `GetAvailable`, `GetPartiallyAchieved`. Still visible in `GetAll`, `GetAchieved` and `GetStatus`.

### State reconciliation

On `Initialize()`:
- definitions are validated (results go to the Unity log) and normalized: null entries and achievements without an ID are skipped, null arrays become empty, on duplicate IDs the first definition wins;
- saved state is loaded; unlocks of achievements no longer in the config are ignored and do not appear in any query;
- stat-driven progress is derived from `IStatSystem`, dependency-based objectives are evaluated against the current unlock set.

An invalid config does not stop the system — achievements affected by an error may simply never unlock.

### Reset

`ResetAll()` deletes saved state through `IAchievementStateStore.DeleteAllAsync()` and brings the system to the state of a fresh `Initialize()` with an empty save. Stats are not reset, so achievements whose goals are already met unlock again right away.

### Validator

`AchievementValidator.Validate()` checks: null entries and missing IDs, duplicate achievement and objective IDs, null objectives, objectives with both/neither StatPath+Required, StatTarget <= 0, references to undefined achievements in `Prerequisites` and `Required` (warning), self-references (error), circular dependencies through any mix of `Prerequisites` and `Required` (topological sort), empty achievements (warning).