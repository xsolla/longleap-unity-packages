# LongLeap Daily Quests

Daily quests modelled on `com.longleap.achievements`: quests are defined in JSON, progress is derived from `IStatSystem`, state is kept through `IDailyQuestStateStore`. Differences from achievements:

- quests are **issued as a set per period** (from one UTC reset to the next) out of a pool, not all available at once;
- progress is counted **from the moment the quest is issued** by default (`Delta`), not over the player's lifetime;
- the **player claims** the reward (`ClaimAsync`) — it is never granted on its own.

## Adding a quest

Add an object to the JSON — that's it. No code changes needed.

```json
{
  "settings": { "dailyCount": 3, "resetHourUtc": 0 },
  "quests": [
    {
      "id": "grind_50",
      "weight": 1,
      "objectives": [ { "statPath": "grind", "statTarget": 50 } ],
      "rewards":    [ { "key": "soft", "amount": 5 } ]
    }
  ]
}
```

| Field | Required | Meaning |
|---|---|---|
| `settings.dailyCount` | no (3) | How many quests are in a set |
| `settings.resetHourUtc` | no (0) | UTC hour at which the set is replaced |
| `settings.uniqueCategories` | no (false) | Quests in a set share no category |
| `settings.hardestLast` | no (false) | The set goes from easy to hard; without `slotDifficulties` — exactly one quest of the highest difficulty |
| `settings.slotDifficulties` | no ([]) | Difficulty of each place in the set, e.g. `[1, 2, 3]` |
| `quests[].id` | yes | Unique id; the game also uses it as the localization key |
| `quests[].weight` | no (1) | Selection weight. 0 — quest is disabled |
| `quests[].categories` | no ([]) | Categories for `uniqueCategories`. A quest without categories never conflicts |
| `quests[].difficulty` | no (0) | Difficulty for `slotDifficulties` and `hardestLast` |
| `objectives[].statPath` | yes | Stat path, same as in achievements (`grind`, `kill:*:grunt`) |
| `objectives[].statTarget` | yes | Amount to reach, > 0 |
| `objectives[].mode` | no (`delta`) | `delta` — growth since the quest was issued; `absolute` — the stat value as is |
| `objectives[].id` | no (`o0`, `o1`…) | Only needed for multiple objectives when you want stable ids |
| `rewards[].key` / `amount` | yes / > 0 | What to grant. The game interprets the key (in Climber — a currency id) |

`delta` is meant for cumulative stats (counters). For Max stats (`score_best`) it means "beat your record by N", which is usually not what you want. For "score 10k in one run", add a stat the game resets at the start of each run and use `absolute`.

`DailyQuestValidator` checks the config at startup: errors and warnings go to the log, and a quest with an error simply stays out of the pool. A typo in `statPath` (stat not declared) is a warning: such a quest can never be completed.

## Selection rules

Without `uniqueCategories`, `hardestLast` and `slotDifficulties` a set is a plain weighted pick from the pool. With any of them, the pick follows the rules and the set is ordered by difficulty, easy to hard.

- **`slotDifficulties`** sets the difficulty of each place. Fewer slots than `dailyCount` — the remaining places take any difficulty (with `hardestLast` — easier than the hardest quest in the pool). More slots — the hardest ones are used. Works without the other two flags too.
- **`hardestLast`** without slots: the set has exactly one quest of the pool's highest difficulty, the rest are easier.
- **`uniqueCategories`**: no two quests in the set share a category.

If a full set cannot satisfy every rule (not enough quests of the needed difficulty or categories), the rules are relaxed one at a time:

1. without `uniqueCategories` — rewards depend on difficulty, so it matters more than variety;
2. without difficulty (`slotDifficulties` and `hardestLast`), but with `uniqueCategories`;
3. without any rules.

The player always gets as many quests as the pool allows — rules never leave a set empty or short. The search is bounded (`DailyQuestSelector.SearchBudget`), so a large pool with impossible rules will not hang the game.

The validator warns when the rules cannot be met on the current pool: a slot asks for a difficulty no quest has; there are more slots than `dailyCount`; a full set cannot satisfy every rule.

## Public API

| Type | What it is |
|---|---|
| `IDailyQuestService` / `DailyQuestService` | The service. `GetActive()`, `GetStatus(id)`, `ClaimAsync(id)`, `Tick()`, `ClaimableCount`, `TimeUntilReset`, `Changed` event |
| `IDailyQuestDebug` | Debugging: `ForceComplete`, `SkipToNextPeriod`, `ResetState`. Same `DailyQuestService`, but a separate interface — game UI does not see it |
| `IDailyQuestProvider` | Where the config comes from. Built-in: `ResourcesDailyQuestProvider`, `JsonDailyQuestProvider` |
| `IDailyQuestStateStore` | Where state is kept. `LoadAsync` returns `DailyQuestStateLoadResult`. Built-in: `JsonDailyQuestStateStore(IPersistentStorage)` |
| `DailyQuestStateLoadResult` | Load outcome: `Loaded` (+ `State`), `Empty`, `Unreadable`, `Unavailable` |
| `IDailyQuestGameBridge` | `OnQuestsRotated`, `OnQuestCompleted`, `ApplyRewards` — the game grants the reward here. Required |
| `QuestStatus` | Quest snapshot for UI: `Definition`, `State` (`InProgress`/`Completed`/`Claimed`), `ObjectiveProgresses`, `TotalProgress`, `CanClaim` |
| `QuestId`, `QuestObjectiveId` | Newtypes with `None`. The reward key is the shared `LongLeap.Core.RewardKey` |
| `DailyQuestJson` | Parses JSON → `DailyQuestConfig` |
| `DailyQuestValidator` | Config validation |
| `DailyQuestSelector` | Weighted deterministic set selection following the rules |

## Setup

Dependencies: `com.longleap.core` 0.2.0, `com.longleap.stats` 0.1.0, `com.unity.nuget.newtonsoft-json` 3.0.2.

```csharp
var service = new DailyQuestService(
    provider: new ResourcesDailyQuestProvider(),             // Resources/DailyQuests/daily_quests.json
    store:    new JsonDailyQuestStateStore(persistentStorage),
    bridge:   myBridge,                                       // required: ApplyRewards arrives here
    stats:    statSystem);                                    // optional; without it there is no progress
await service.InitializeAsync();

services.Register<IDailyQuestService>(service);
services.Register<IDailyQuestDebug>(service);                 // for dev tools
```

`ResourcesDailyQuestProvider` reads a `TextAsset` via `Resources.Load` — the JSON is inside the build, no HTTP, so it also works on portals that repackage the build (GamePix). It reads once and caches the task: you can create it and call `LoadAsync()` early in loading (on the main thread), then hand it to the service later — the service gets the ready result. Missing file or broken JSON — empty pool and an error in the log.

A custom store returns `DailyQuestStateLoadResult`: no save yet — `Empty()`, cannot be read — `Unreadable()`, storage did not respond — `Unavailable()`. String parsing is ready-made: `JsonDailyQuestStateStore.Parse(json)`.

## Using it in UI

```csharp
service.Changed += Redraw;

void Redraw()
{
    foreach (var quest in service.GetActive())
    {
        var p = quest.TotalProgress;          // p.Current / p.Target, p.Ratio
        claimButton.interactable = quest.CanClaim;
    }
    timerLabel.text = service.TimeUntilReset.ToString(@"hh\:mm");
}

void Update() => service.Tick();   // on reset, a new set arrives along with Changed

async void OnClaimClicked(QuestId id) => await service.ClaimAsync(id);   // the reward arrives in bridge.ApplyRewards
```

`GetActive()` returns the same array and the same `QuestStatus` objects until something changes — no need to copy them, but do not modify them. A reference comparison (`ReferenceEquals(old, status)`) is a cheap way to tell that a row does not need redrawing.

## How it works

- **Period change is lazy.** There is no timer: every `GetActive`/`ClaimAsync`/`Tick`/stat change checks whether a new period has started. An open quest UI calls `Tick()` from `Update` so the set changes in front of the player. Moving the clock back does not change the set.
- **The baseline is saved, not the progress.** When a quest is issued, the current stat value is stored (`baseline`); progress = stat − baseline. So progress survives a restart and nothing is written on every grind — writes happen only on issue, completion and claim.
- **A stat rolled back — the baseline goes down.** If a `delta` stat drops below the baseline (stats save older than the quests save, stats were reset), the baseline becomes the current value and is saved. Otherwise progress would sit at zero until the stat caught up with the old mark. Progress made before the rollback is not restored.
- **Completion latches — per objective.** A met objective stays met even if the stat later drops; the quest becomes `Completed` once all are met. So "10k in one run + 30 grinds" counts even if the grinds were finished in another run.
- **Claiming is crash-safe.** The quest is marked claimed first, `ClaimAsync` waits for the write and only then calls `ApplyRewards`: at worst the player loses one reward, but never gets it twice. Write failed — no reward, `false`, the player can press again. The bridge is required — without it there would be nowhere to grant the reward.
- **Writes are queued.** Each save waits for the previous one, so an older state never overwrites a newer one.
- **The set is deterministic.** The seed is the period start; quests from the previous set are picked only when the set cannot be filled without them.
- **Config changed mid-period.** Removed quests drop out of the set, new objectives count from the moment of loading, and if `dailyCount` grew the set is topped up by the same rules: remaining quests keep their slots and categories.
- **Storage did not respond** (`Unavailable`) — the service is off until restart, so an already claimed reward is not issued again.
- **Save cannot be read** (`Unreadable`: corrupted or from a newer version) — no quests until the next reset: it is unknown what was already claimed today, and a freshly issued set would match the old one.
- **Stat changes are cheap.** Only quests waiting on that stat are checked; nothing is allocated when nothing changed. `Changed` fires only when visible progress or state moved; `QuestStatus` is rebuilt lazily, on read. `GetActive()` also reconciles progress with stats, so a stat changed without an event is visible right away.

Internals: `LongLeap.Core.ResetSchedule` (when a period starts), `ProgressEvaluator` (baseline, progress, latches, rollback) and `DailyQuestSelector` (set selection) — free of service state, each tested separately; `DailyQuestService` ties them together.
