# LongLeap Achievements

Achievement system: define goals, track progress via stat subscriptions, unlock on completion.

**Achievement**:
A named goal with zero or more **objectives** and optional **prerequisites**. Defined in data; tracked at runtime via stat subscriptions. Status: unavailable (prerequisites not met) → available → unlocked. Unlocks are permanent.

**Objective**:
A single goal within an achievement. Either stat-driven (e.g. "kill 100 enemies": `StatPath` + `StatTarget`) or dependency-based (`Required`: other achievements that must be unlocked).

**Progress**:
A value (0..target) representing how close an objective is to completion. Stat-driven objectives update on stat change events; dependency-based ones count unlocked required achievements. When prerequisites are met and all objectives are complete, the achievement unlocks.

## Relationships

- **Achievements → Stats**: `AchievementSystem` subscribes to `IStatSystem` to derive objective progress from stat changes.
- **Achievements → Game**: unlocks and availability are reported through `IAchievementGameBridge`; the game decides what to show (in Climber — a toast via `NotificationBootstrapper`).
- **Achievements → Storage**: unlocks are persisted through `IAchievementStateStore`, implemented by the game.
