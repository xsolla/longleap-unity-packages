# LongLeap Daily Rewards

Daily reward calendar system with streak tracking, configurable cycles, grace periods, and cloud-backed state persistence.

## Install (Unity Package Manager)

Unity 6 (6000.0 – 6000.4), WebGL build target. In Unity: **Window → Package Manager → + → Add package from git URL**. Add the packages in this order: a package installed from Git does not pull in other Git packages, so the LongLeap packages it needs go first.

```
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.core
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.dailyrewards
```

Pin a version by adding `#v1.0.0` at the end of each URL. Unity packages this one uses are installed automatically. All packages and their dependencies: [root README](../README.md#install-unity-package-manager).

## Quick start

1. **Add the packages** to `Packages/manifest.json` (`com.longleap.core` is required; Newtonsoft comes as a dependency):

   ```json
   "com.longleap.core": "file:LongLeap/com.longleap.core",
   "com.longleap.dailyrewards": "file:LongLeap/com.longleap.dailyrewards"
   ```

   Optionally add `"com.longleap.dailyrewards"` to `"testables"` to see the package tests in the Test Runner.

2. **Reference the assemblies** from your asmdef: `LongLeap.DailyRewards` and `LongLeap.Core` (`RewardKey` lives in core).

3. **Create the config**: *Assets → Create → LongLeap → Daily Rewards → Config*. Fill `Days` in order (each day: reward entries with a key the game understands, e.g. a currency id `soft`, and an amount), then `CyclePolicy`, `ResetHourUtc`, `GracePeriod` (see [Data Sources](#data-sources)). The inspector warns about mistakes (empty days, missing keys, zero amounts).

4. **Pick a state store** (`IDailyRewardsStateStore`):
   - `new JsonDailyRewardsStateStore(persistentStorage)` on top of `LongLeap.Core.IPersistentStorage`, or
   - your own store (e.g. cloud saves): read the JSON, return `JsonDailyRewardsStateStore.Parse(json)`; if the storage throws, return `DailyRewardsStateLoadResult.Unavailable()` — **never** `Empty`, or a claimed day could be granted twice.

5. **Implement the game bridge** — one method that grants the reward (see [Game bridge](#game-bridge)).

6. **Bring the service up** during loading and register it only when ready:

   ```csharp
   var service = new DailyRewardsService(config, store, bridge);   // optional 4th arg: Func<DateTime> utcNow
   await service.InitializeAsync();
   if (service.IsReady)                                            // false: store did not answer or no days
   {
       services.Register<IDailyRewardsService>(service);           // game UI
       services.Register<IDailyRewardsDebug>(service);             // dev tools: SkipToNextResetWindow, ResetState
   }
   ```

   Without a registered service the game should simply hide the rewards entry point.

7. **Show the UI**, either:
   - the ready-made `DailyRewardsScreen` from `com.longleap.ui`: call `screen.Bind(service, sounds, rewardIcon)` when the window is created; or
   - your own: draw `GetCalendar()`, call `await ClaimAsync()` on a claimable day, re-read on `Changed`, call `Tick()` from `Update` while open, and use `TimeUntilNextClaim` for a countdown (see [UI updates](#ui-updates)).

**Reference integration (Climber):** `Assets/Scripts/Bootstrap/Loading/Tasks/DailyRewardsTask.cs` (service, wallet bridge, registration), `Assets/Scripts/DailyRewards/CloudSaveDailyRewardsStateStore.cs` (cloud-save store), `Assets/Scripts/Bootstrap/Loading/Tasks/MainMenuTask.cs` (binding `DailyRewardsScreen`).

## Data Sources

Config via ScriptableObject (`DefaultDailyRewardConfigSO`). State persisted as JSON via `IDailyRewardsStateStore` -> `JsonDailyRewardsStateStore`.

| SO field | Default | Description |
|---|---|---|
| `Days[]` | — | Calendar days in order (`DailyRewardDayDef`: reward entries). The number of days is the list length, the day index is the position. Empty list disables the service |
| `CyclePolicy` | `Loop` | `Once` (completes permanently) or `Loop` (wraps) |
| `ResetHourUtc` | 0 | UTC hour when next claim window opens |
| `GracePeriod` | 24h | Strict streak by default: a missed day resets it. Each extra 24h forgives one more missed day (use multiples of 24h) |

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `IDailyRewardsService` | Interface | `Changed` event, `InitializeAsync()`, `IsReady`, `TimeUntilNextClaim`, `Tick()`, `CanClaimToday`, `IsCycleComplete`, `Streak`, `MaxConsecutiveStreak`, `ClaimAsync()` -> Task<bool>, `GetCalendar()` -> CalendarDay[] |
| `IDailyRewardsDebug` | Interface | `SkipToNextResetWindow()`, `ResetState()` — dev tools and tests only, kept off the game-facing service. `DailyRewardsService` implements both; register it under both interfaces |
| `DailyRewardsService` | Sealed class | Constructor: `(IDailyRewardsConfig, IDailyRewardsStateStore, IDailyRewardsGameBridge, Func<DateTime> utcNow = null)`. `utcNow` defaults to `DateTime.UtcNow` (inject a fake in tests or server time in the game). Validates the config and snapshots the days in `InitializeAsync`. Auto-disables if there are no days |
| `IDailyRewardsConfig` | Interface | `Days` (`IReadOnlyList<DailyRewardDay>`), `CyclePolicy`, `ResetHourUtc`, `GracePeriod` |
| `DailyRewardsConfigValidator` | Static class | `Validate(IDailyRewardsConfig)` -> `Errors[]`, `Warnings[]`. Used by the service at init and by the SO in `OnValidate` |
| `DefaultDailyRewardConfigSO` | ScriptableObject | `[CreateAssetMenu(menuName = "LongLeap/Daily Rewards/Config")]` |
| `IDailyRewardsStateStore` | Interface | `LoadAsync()` -> DailyRewardsStateLoadResult, `SaveAsync(DailyRewardsState)` |
| `DailyRewardsStateLoadResult` | Readonly struct | `Status` (`Loaded` / `Empty` / `Unreadable` / `Unavailable`) + `State` |
| `JsonDailyRewardsStateStore` | Sealed class | Constructor: `(IPersistentStorage, string storageKey = null)`. Static `Parse(json)` — shared by other stores (cloud saves) |
| `DailyRewardsState` | Sealed class | `CurrentVersion` (2), `Empty()`, `Version`, `ClaimedCount` (days 0..N-1 of the current cycle), `Streak`, `MaxConsecutiveStreak`, `LastClaimUtcTicks`, `CurrentCycleNumber` |
| `IDailyRewardsGameBridge` | Interface | `ApplyReward(DailyRewardClaimResult)` — the only duty: grant the reward |
| `DailyRewardDay` | Sealed class | `DayIndex` (position), `Rewards` (`IReadOnlyList<RewardEntry>`) |
| `DailyRewardClaimResult` | Sealed class | `Day`, `Streak`, `MaxStreak`, `StreakWasReset`, `CompletedCycle`, `CycleNumber`, `IsCycleComplete` |
| `CalendarDay` | Sealed class | View-model: `Day` + `State` |
| `RewardEntry` | Sealed class | `Key` (RewardKey), `RewardId`, `Amount` |
| `CalendarDayState` | Enum | `Claimed`, `Claimable`, `Locked`, `Future` |
| `DailyRewardCyclePolicy` | Enum | `Once`, `Loop` |
| `RewardKey` | Readonly struct (`LongLeap.Core`) | Shared with daily quests. `None` sentinel |
| `RewardId` | Readonly struct `: IEquatable<RewardId>` | Typed newtype. `None` sentinel |

## Initialization

```csharp
var config = Resources.Load<DefaultDailyRewardConfigSO>("Path/To/Config");
var store = new JsonDailyRewardsStateStore(persistentStorage);
var service = new DailyRewardsService(config, store, bridge);
await service.InitializeAsync();
if (!service.IsReady)
    return; // store did not answer or no days — do not expose the calendar
```

### UI updates

Subscribe to `Changed` and re-read `GetCalendar()` in the handler. While the rewards UI is open, call `Tick()` from `Update` — it raises `Changed` once when a new reset window starts. `TimeUntilNextClaim` drives a countdown (`Zero` when claimable now or there is nothing to wait for).

## Usage

### Claiming

```csharp
if (service.CanClaimToday)
{
    if (await service.ClaimAsync())
    {
        // the claimed day is persisted; bridge.ApplyReward already fired
        UpdateCalendarUI(service.GetCalendar());
    }
    // false: nothing to claim, a claim already in flight, or the save failed —
    // in that case nothing was granted and the day stays claimable
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
    // result.StreakWasReset / CompletedCycle / CycleNumber — for analytics or a toast
}
```

## Internals

`DailyRewardsService` owns I/O only: loading and serialized saving, the clock, `Changed`, the bridge. The calendar rules live in the internal `DailyRewardsRules` — no storage, no events, time passed in, input state never mutated (`TryClaim` returns a new state). Rules are unit-tested directly (`DailyRewardsRulesTests`).

### Claim flow

1. Check `CanClaimToday` (UTC past reset boundary, no claim in flight)
2. `DailyRewardsRules.TryClaim` builds the next state: grace period check (streak may reset), `ClaimedCount = day + 1`, streak++, last claim time
3. Cycle check: last day claimed -> `CurrentCycleNumber++`. `Once` -> permanently blocked. `Loop` -> `ClaimedCount` stays at the day count, so the finished cycle stays visible (all `Claimed`) until the next reset; once a new window opens, `GetCalendar` shows a fresh cycle and the next claim starts it at day 0
4. Await the save of the next state. While it is in flight `CanClaimToday` is false. If it fails, the current state is untouched, nothing is granted, `ClaimAsync` returns false
5. Commit the next state
6. Call `ApplyReward(result)`; the result carries `StreakWasReset`, `CompletedCycle`, `CycleNumber`. An exception there is logged and does not roll the claim back. Then `Changed` is raised

Save-then-grant: a crash between the save and `ApplyReward` loses one reward but never grants it twice. Saves are serialized, so an older state never overwrites a newer one.

### Reset window

`LongLeap.Core.ResetSchedule` (shared with daily quests) built from `ResetHourUtc`. A claim is allowed when `LastClaimUtcTicks < PeriodStart(now).Ticks`.

### State persistence

`LoadAsync` reports how reading went:

| Status | When | Service |
|---|---|---|
| `Loaded` | save read | uses it |
| `Empty` | no save yet | fresh calendar |
| `Unreadable` | corrupt or newer `Version` | fresh calendar, today's window counted as used (it may have been claimed already), saved immediately; claimable after the next reset |
| `Unavailable` | store threw | `IsReady = false` until restart — a fresh start could grant a claimed day twice |

Older formats are migrated in `Parse`: v1 (`ClaimedDayIndices` array) -> v2 (`ClaimedCount` = array length). A save without `Version` is treated as v1.

`Unreadable`/`Unavailable` must never be turned into `Empty` by a store. `JsonDailyRewardsStateStore.Parse` implements the classification; game stores (e.g. cloud saves) should reuse it.