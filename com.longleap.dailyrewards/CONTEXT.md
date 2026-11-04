# LongLeap Daily Rewards

Daily rewards system with streak tracking, cycle management, and configurable claim windows.

**Streak**:
Consecutive days the player has claimed a reward. A missed day breaks it: the streak resets to 0 on the next claim (with the default **grace period**). Tracked per-player in persistent state.
_Avoid_: Chain, sequence, run

**Grace Period**:
How far before the last reset boundary the last claim may lie for the streak to survive. The default (24h) means a strict streak: missing any day resets it. Each further 24h forgives one more missed day; use multiples of 24h. When the streak resets, cycle progress (which days were claimed) is preserved.

**Cycle**:
A complete pass through all reward days. When a cycle ends, it either completes permanently (`Once` policy) or wraps back to day 1 (`Loop` policy, firing a cycle-completed event). In `Loop` the finished cycle stays on screen until the next reset; the new cycle starts with the next reset window.

**Reset Window**:
A fixed UTC hour when the daily reward becomes available again. Time-math (comparing `lastClaimUtcTicks` to the last reset boundary) is fully encapsulated inside the service.

**RewardKey** / **RewardId**:
Typed newtypes for reward identification. `RewardKey` (from `LongLeap.Core`, shared with daily quests) identifies what to grant (e.g. a currency id `"soft"`); the game interprets it. `RewardId` identifies a specific reward item. Both follow the `readonly struct : IEquatable<T>` pattern with `None` sentinel values.

**CalendarDay**:
A view-model joining a reward day definition with its current state: `Claimed`, `Claimable`, `Locked`, or `Future`. Produced when the game asks the service "what does the calendar look like right now?"
