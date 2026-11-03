# LongLeap Daily Quests

Daily quests: every reset window the player gets a small set of quests from a pool; progress comes from stats; the player claims each reward by hand.

**Quest**:
A named task with one or more **objectives** and a list of **rewards**. Defined in data (JSON). Lives in the **pool** until picked into the **active set**.
_Avoid_: Mission, task, challenge

**Pool**:
All playable quests from the config. Quests with errors (see `DailyQuestValidator`) never enter the pool.

**Active set**:
The quests issued for the current **period** — `DailyCount` of them, picked by weight from the pool, preferring quests that were not in the previous set, and following the **selection rules** if any are set.

**Selection rules**:
Optional constraints on the active set: **slot** difficulties (`SlotDifficulties`), exactly one hardest quest (`HardestLast`), no shared **category** (`UniqueCategories`). With rules the set is ordered by difficulty, easy to hard.

**Slot**:
One place in the active set with a required **difficulty**. Slots fewer than `DailyCount` leave the rest open to any difficulty.

**Category** / **Difficulty**:
Tags of a quest, used only by the selection rules. A quest without categories never conflicts.

**Relaxation**:
What the selector does when a full set cannot meet every rule: drop unique categories first, then difficulty, then all rules. The set is never left short because of rules — only because the pool is too small.
_Avoid_: Fallback

**Period**:
The time between two **reset windows** (`ResetHourUtc`). When it ends the active set is replaced — unclaimed rewards are lost.
_Avoid_: Day (a period does not have to start at midnight)

**Objective**:
One measurable goal: a stat path and a target. In `Delta` mode progress is "how much the stat grew since the quest was issued" (counted from a stored **baseline**); in `Absolute` mode it is the stat value itself.

**Baseline**:
The stat value at the moment the quest was issued. Persisted, so progress survives restarts without storing progress itself. If a `Delta` stat drops below it (rolled-back stats save), the baseline is lowered to the current value — **rebase**.

**Claim**:
The player taking the reward of a completed quest. The only moment rewards are granted — completing a quest never grants anything by itself.
_Avoid_: Collect, redeem

## Quest lifecycle

`InProgress` → `Completed` (all objectives met; each objective is latched on its own — it stays met even if the stat later drops) → `Claimed` (only after the claim is persisted).

## Relationships

- **DailyQuests → Stats**: `DailyQuestService` subscribes to `IStatSystem` and derives objective progress from it.
- **DailyQuests → Game**: rewards are opaque keys; the game turns them into currency/items in `IDailyQuestGameBridge.ApplyRewards`. The bridge is required.
- **DailyQuests → Store**: `IDailyQuestStateStore.LoadAsync` says how loading went (`DailyQuestStateLoadResult`): `Loaded`, `Empty`, `Unreadable` (no set until the next reset), `Unavailable` (service off).
- **DailyQuests → Dev tools**: debug commands live on `IDailyQuestDebug`, not on `IDailyQuestService`.
