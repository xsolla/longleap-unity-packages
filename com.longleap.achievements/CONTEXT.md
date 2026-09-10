# LongLeap Achievements

Achievement system: define goals, track progress via stat subscriptions, unlock on completion.

**Achievement**:
A named goal with one or more **objectives**. Defined in data; tracked at runtime via stat subscriptions. Has a status: Locked → InProgress → Unlocked.

**Objective**:
A single measurable goal within an achievement (e.g. "kill 100 enemies", "reach level 10"). Each objective tracks its own progress.

**Progress**:
A value (0..target) representing how close an objective is to completion. Updated by stat change events. When all objectives reach their targets, the achievement unlocks.

## Relationships

- **Achievements → Stats**: `AchievementSystem` subscribes to `IStatSystem` to auto-derive objective progress from stat changes.
- **Achievements → Notifications**: Unlock events fire notifications via `INotificationService`.
