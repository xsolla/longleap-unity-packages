# LongLeap Daily Rewards

Daily rewards system with streak tracking, cycle management, and configurable claim windows.

**Streak**:
Consecutive days the player has claimed a reward. Resets to 0 if the elapsed time since the last claim exceeds the **grace period**. Tracked per-player in persistent state.
_Avoid_: Chain, sequence, run

**Grace Period**:
A time window after the last claim during which the streak is preserved even if a day is missed. If the period expires without a claim, streak resets but cycle progress (which days were claimed) is preserved.

**Cycle**:
A complete pass through all reward days. When a cycle ends, it either completes permanently (`Once` policy) or wraps back to day 1 (`Loop` policy, firing a cycle-completed event).

**Reset Window**:
A fixed UTC hour when the daily reward becomes available again. Time-math (comparing `lastClaimUtcTicks` to the last reset boundary) is fully encapsulated inside the service.

**RewardKey** / **RewardId**:
Typed newtypes for reward identification. `RewardKey` identifies the reward category (e.g. `"xp"`, `"gold"`); `RewardId` identifies a specific reward item. Both follow the `readonly struct : IEquatable<T>` pattern with `None` sentinel values.

**CalendarDay**:
A view-model joining a reward day definition with its current state: `Claimed`, `Claimable`, `Locked`, or `Future`. Produced when the game asks the service "what does the calendar look like right now?"
