# LongLeap Daily Rewards

Daily reward calendar system with streak tracking, configurable cycles, grace periods, and cloud-backed state persistence.

## Data Sources

Config via ScriptableObject (`DefaultDailyRewardConfigSO`). State persisted as JSON via `IDailyRewardsStateStore` -> `JsonDailyRewardsStateStore`.

| SO field | Default | Description |
|---|---|---|
| `DayCount` | 7 | Number of reward days. 0 disables the service |
| `Days[]` | — | Array of `DailyRewardDayDef` (day index + reward entries) |
| `CyclePolicy` | `Loop` | `Once` (completes permanently) or `Loop` (wraps) |
| `ResetHourUtc` | 0 | UTC hour when next claim window opens |
| `GracePeriod` | 24h | Window after last claim where streak survives a missed day |

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `IDailyRewardsService` | Interface | `InitializeAsync()`, `CanClaimToday`, `IsCycleComplete`, `Streak`, `MaxConsecutiveStreak`, `Claim()` -> bool, `GetCalendar()` -> CalendarDay[], `Reset()`, `DebugSkipToNextResetWindow()` |
| `DailyRewardsService` | Sealed class | Constructor: `(IDailyRewardsConfig, IDailyRewardsStateStore, IDailyRewardsGameBridge)`. Auto-disables if `DayCount <= 0` |
| `IDailyRewardsConfig` | Interface | `DayCount`, `Days`, `CyclePolicy`, `ResetHourUtc`, `GracePeriod` |
| `DefaultDailyRewardConfigSO` | ScriptableObject | `[CreateAssetMenu(menuName = "LongLeap/Daily Rewards/Config")]` |
| `IDailyRewardsStateStore` | Interface | `LoadAsync()` -> DailyRewardsState, `SaveAsync(DailyRewardsState)` |
| `JsonDailyRewardsStateStore` | Sealed class | Constructor: `(IPersistentStorage, string storageKey = null)`. Version checking, corrupt-data fallback |
| `DailyRewardsState` | Sealed class | `Version`, `ClaimedDayIndices[]`, `Streak`, `MaxConsecutiveStreak`, `LastClaimUtcTicks`, `CurrentCycleNumber` |
| `IDailyRewardsGameBridge` | Interface | `ApplyReward(DailyRewardClaimResult)`, `OnStreakChanged(int, int)`, `OnCycleCompleted(int)`, `OnStreakReset()` |
| `DailyRewardDay` | Sealed class | `DayIndex`, `Rewards[]` |
| `DailyRewardClaimResult` | Sealed class | `Day`, `Streak`, `MaxStreak`, `IsCycleComplete` |
| `CalendarDay` | Sealed class | View-model: `Day` + `State` |
| `RewardEntry` | Sealed class | `Key` (RewardKey), `RewardId`, `Amount` |
| `CalendarDayState` | Enum | `Claimed`, `Claimable`, `Locked`, `Future` |
| `DailyRewardCyclePolicy` | Enum | `Once`, `Loop` |
| `RewardKey` | Readonly struct `: IEquatable<RewardKey>` | Typed newtype. `None` sentinel |
| `RewardId` | Readonly struct `: IEquatable<RewardId>` | Typed newtype. `None` sentinel |

## Initialization

```csharp
var config = Resources.Load<DefaultDailyRewardConfigSO>("Path/To/Config");
var store = new JsonDailyRewardsStateStore(persistentStorage);
var service = new DailyRewardsService(config, store, bridge);
await service.InitializeAsync();
```

## Usage

### Claiming

```csharp
if (service.CanClaimToday)
{
    if (service.Claim())
    {
        // bridge.ApplyReward already fired with result
        // bridge.OnStreakChanged already fired if streak changed
        UpdateCalendarUI(service.GetCalendar());
    }
}
```

### Reading the calendar

```csharp
CalendarDay[] calendar = service.GetCalendar();
foreach (var cd in calendar)
{
    // cd.State: Claimed | Claimable | Locked | Future
}
```

### Game bridge

```csharp
public sealed class MyGameBridge : IDailyRewardsGameBridge
{
    public void ApplyReward(DailyRewardClaimResult result)
    {
        foreach (var entry in result.Day.Rewards)
            GrantReward(entry.Key, entry.RewardId, entry.Amount);
    }
    public void OnStreakChanged(int streak, int maxStreak) { }
    public void OnCycleCompleted(int cycleNumber) { }
    public void OnStreakReset() { }
}
```

## Internals

### Claim flow

1. Check `CanClaimToday` (UTC past reset boundary)
2. Grace period check — if elapsed > `GracePeriod`, streak resets
3. Mark day claimed, increment streak
4. Fire `bridge.OnStreakChanged`, then `bridge.ApplyReward(result)`
5. Cycle check: `Once` -> permanently blocked; `Loop` -> clear indices, fire `OnCycleCompleted`

### Reset window

Computed from `ResetHourUtc`. A claim is allowed when `lastClaimUtcTicks < lastResetBoundary.Ticks`.

### State persistence

`JsonDailyRewardsStateStore` loads/saves via `IPersistentStorage`. Corrupt/missing data -> returns empty state. Future-version data -> empty state (forward-compat).