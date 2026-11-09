# LongLeap Stats

Hierarchical stat system with wildcard subscriptions, accumulate/max cascading, and cloud save persistence.

## Data Sources

Leaf values (`Value`, `MaxValue`) are saved one by one through an `IStatPersistence` the game provides; non-leaf aggregates are recalculated on load. The package ships no persistence — a cloud-save one typically stores each leaf under its own key, e.g. `stat_kill:monk:grunt` → `"3"`, `stat_bighit:monk` → `"50"`.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `StatType` | Enum | `Value`, `MaxValue` (leaves), `Accumulate`, `Max` (branches) |
| `StatPath` | Readonly struct `: IEquatable<StatPath>` | Colon-delimited hierarchical key. Validation, `Parent`, `Append`, `Matches` with `*` wildcard. Implicit from `string` |
| `IStatSystem` | Interface | `Subscribe(StatPath, Action<StatPath, long>)`, `Unsubscribe`, `UnsubscribeAll`, `GetAggregate(StatPath)` -> long |
| `IStatPersistence` | Interface | Game-implemented storage: `SaveAsync(StatPath, long)`, `LoadAsync(StatPath)` -> `StatValue` (`StatValue.None` when nothing is stored) |
| `StatsSystem` | Sealed class `: IStatSystem` | Full implementation. Thread-safe. `Define`, `Set`, `Add`, `Get`, `SaveAsync`, `LoadAsync` |

## Initialization

```csharp
var stats = new StatsSystem();

// Parents must be defined before children
stats.Define("kill", StatType.Accumulate);
stats.Define("kill:monk", StatType.Accumulate);
stats.Define("kill:monk:grunt", StatType.Value);
stats.Define("bighit", StatType.Max);
stats.Define("bighit:monk", StatType.MaxValue);

IStatPersistence persistence = myPersistence;  // game-implemented, e.g. over cloud saves
await stats.LoadAsync(persistence);  // restores saved values
```

## Usage

### Set and Add

```csharp
stats.Set("kill:monk:grunt", 3);   // Value -> assigns directly
stats.Set("bighit:monk", 50);      // MaxValue -> max(current, 50)
stats.Add("kill:monk:grunt", 2);   // Value -> 3 + 2 = 5
```

After `Set`/`Add`, parents recalculate automatically via cascade.

### Subscribe with wildcards

```csharp
stats.Subscribe("kill:*:grunt", (path, value) => {
    // fires for kill:monk:grunt and kill:ninja:grunt
});
```

### GetAggregate

```csharp
long gruntKills = stats.GetAggregate("kill:*:grunt");  // sums matching leaves
long allKills = stats.GetAggregate("kill");             // pre-computed branch value
```

### Persistence

```csharp
await stats.SaveAsync(persistence);  // writes leaves changed since the last successful save
await stats.LoadAsync(persistence);
```

There is no built-in auto-save: `Task.Delay` does not work on WebGL, so the game calls
`SaveAsync` itself (per-frame timer, pause, quit — see `StatsAutoSaver` in Climber).
If a write fails, `SaveAsync` rethrows and the unwritten leaves stay dirty, so the next
call retries them.

## Internals

### Cascade flow

```
Set/Add on leaf -> update node -> dispatch subscriptions -> cascade up parent chain
  Accumulate parent = sum of direct children
  Max parent = max of direct children
  Changed -> continue cascading upward
  Unchanged -> stop (no spurious propagation)
```

### Wildcard aggregate

`DetermineAggregateOp` walks from deepest segment upward to find nearest defined ancestor and uses its `StatType` to decide sum vs max.

### Tree example

```
kill                       (Accumulate)  = 10
+- kill:monk              (Accumulate)  = 10
|  +- kill:monk:grunt    (Value)       = 3
|  +- kill:monk:brute    (Value)       = 7
+- kill:ninja             (Accumulate)  = 0
   +- kill:ninja:grunt   (Value)       = 0

bighit                     (Max)         = 100
+- bighit:monk            (MaxValue)    = 50
+- bighit:ninja           (MaxValue)    = 100
```

### Thread safety

All public methods lock on a private object. Subscription callbacks are invoked inside the lock — keep them fast.