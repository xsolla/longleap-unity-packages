# LongLeap Daily Quests

Daily quests: every reset window the player gets a small set of quests from a pool; progress comes from stats; the player claims each reward by hand.

**Quest**:
A named task with one or more **objectives** and a list of **rewards**. Defined in data (JSON). Lives in the **pool** until picked into the **active set**.
_Avoid_: Mission, task, challenge

**Pool**:
All playable quests from the config. Quests with errors (see `DailyQuestValidator`) never enter the pool.

**Active set**:
The quests issued for the current **period** — `DailyCount` of them, picked by weight from the pool, preferring quests that were not in the previous set.

**Period**:
The time between two **reset windows** (`ResetHourUtc`). When it ends the active set is replaced — unclaimed rewards are lost.
_Avoid_: Day (a period does not have to start at midnight)

**Objective**:
One measurable goal: a stat path and a target. In `Delta` mode progress is "how much the stat grew since the quest was issued" (counted from a stored **baseline**); in `Absolute` mode it is the stat value itself.

**Baseline**:
The stat value at the moment the quest was issued. Persisted, so progress survives restarts without storing progress itself.

**Claim**:
The player taking the reward of a completed quest. The only moment rewards are granted — completing a quest never grants anything by itself.
_Avoid_: Collect, redeem

## Quest lifecycle

`InProgress` → `Completed` (all objectives met; each objective is latched on its own — it stays met even if the stat later drops) → `Claimed` (only after the claim is persisted).

## Relationships

- **DailyQuests → Stats**: `DailyQuestService` subscribes to `IStatSystem` and derives objective progress from it.
- **DailyQuests → Game**: rewards are opaque keys; the game turns them into currency/items in `IDailyQuestGameBridge.ApplyRewards`.
