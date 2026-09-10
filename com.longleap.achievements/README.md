# LongLeap Achievements

Stats-driven achievement system with cascading unlocks, typed IDs, and configurable providers. Achievements are defined by the game (via `IAchievementProvider`); progress is derived from `IStatSystem` at runtime; unlock state is persisted through `IAchievementStateStore`.

## Data Sources

### Achievement definitions

Supplied by `IAchievementProvider.LoadAsync()`. Each objective is either **stat-driven** (`statPath` + `statTarget`) or **dependency-based** (`required` — an array of `AchievementId`s that must unlock first). The two modes are mutually exclusive.

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
| `AchievementStatus` | Sealed class | Full status: `Definition`, `IsAvailable`, `IsUnlocked`, `ObjectiveProgresses` |
| `AchievementStateEntry` | Sealed class, `[Serializable]` | `AchievementId` (string), `IsUnlocked`, `UnlockedTimestamp` |
| `AchievementStatesSnapshot` | Sealed class, `[Serializable]` | `Version` (int), `Entries` |
| `IAchievementSystem` | Interface | `Initialize(provider, stateStore, gameBridge = null, stats = null)`, `Shutdown()`, `GetAll()`, `GetByGroup(GroupId)`, `GetAvailable()`, `GetAchieved()`, `GetPartiallyAchieved()`, `GetStatus(AchievementId)`, `GetGroups()`, `ForceUnlock(AchievementId)`, `ResetAll()`, `Save()`, `IsDirty` |
| `AchievementSystem` | Sealed class | Implementation. Stat subscription, unlock cascading, state reconciliation |
| `IAchievementProvider` | Interface | `Task<AchievementDefinition[]> LoadAsync()`. Game implements |
| `IAchievementGameBridge` | Interface | `OnAvailable(AchievementId)`, `OnUnlocked(AchievementDefinition)`. Optional |
| `IAchievementStateStore` | Interface | `LoadAsync()`, `MarkUnlockedAsync()`, `DeleteAllAsync()`. Game implements |
| `AchievementValidator` | Static class | `Validate(AchievementDefinition[])` -> `ValidationResult` |
| `ValidationResult` | Sealed class | `IsValid` (bool), `Errors` (string[]), `Warnings` (string[]) |

## Initialization

```csharp
var system = new AchievementSystem();
system.Initialize(
    provider: myAchievementProvider,
    stateStore: myStateStore,
    gameBridge: myBridge,   // optional
    stats: myStatSystem     // optional
);
```

After initialization, definitions are loaded and validated, saved unlock state is reconciled, and if `stats` is provided, subscriptions are created for all `statPath` values.

## Usage

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

When `IStatSystem` is passed to `Initialize()`, the system subscribes to stat changes for every unique `statPath`. Progress is recomputed from scratch each time — if a stat value drops, the objective can become incomplete again.

### Cascading unlocks

When an achievement unlocks, the `UnlockPropagator` re-evaluates all dependent achievements. Chain A -> B -> C fully drains in one cycle. Maximum cascade depth bounded by total achievement count.

### Hidden achievements

`IsHidden = true` excludes from `GetByGroup`, `GetAvailable`, `GetPartiallyAchieved`. Still visible in `GetAll` and `GetStatus`.

### State reconciliation

On `Initialize()`: definitions loaded and deduplicated; saved state loaded and entries for removed definitions pruned; stat-driven progress derived from `IStatSystem`; dependency-based objectives evaluated against current unlock set.

### Validator

`AchievementValidator.Validate()` checks: duplicate IDs, objectives with both/neither StatPath+Required, StatTarget<=0, dangling prerequisites, empty achievements, circular prerequisite chains (topological sort).