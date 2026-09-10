# LongLeap Stats

Hierarchical stat tree with accumulation and wildcard subscriptions. Replaced the old event/matcher achievement layer.

**StatPath**:
Typed newtype for hierarchical stat paths (e.g. `"kill:monk:grunt"`). Supports wildcard matching (`*`).

**StatType**:
Enum: `Value` (settable), `MaxValue` (auto-tracks maximum), `Accumulate` (auto-sums children on write), `Max` (auto-tracks maximum of children).

**StatsSystem**:
Implements `IStatSystem`. Defines stat paths with types, auto-cascades writes from leaf to ancestors. Subscriptions with wildcard pattern matching. Cloud-persisted with debounced auto-save.

**IStatSystem**:
Interface: `Define(path, type)`, `Set(path, value)`, `Get(path)`, `Subscribe(pattern, callback)`.

## Relationships

- **Stats → Achievements**: Achievement system subscribes to stat changes via `IStatSystem.Subscribe()` to derive objective progress.
- **Stats → Cloud Saves**: `StatsSystem` persists stats via `ICloudSaveService`.
