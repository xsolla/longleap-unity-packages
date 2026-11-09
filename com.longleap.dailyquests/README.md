# LongLeap Daily Quests

Daily quests modelled on `com.longleap.achievements`: quests are described in JSON, progress is derived from `IStatSystem`, state is stored through `IDailyQuestStateStore`. How they differ from achievements:

- quests are **issued as a set per period** (from one UTC reset to the next), picked from a pool, rather than all being active at once;
- progress counts **from the moment of issue** (`delta`) by default, not over the player's lifetime;
- the reward is **claimed by the player** (`ClaimAsync`) — it is not granted automatically.

## Adding a quest

Add an object to the JSON — that's all. No code changes needed.

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
| `settings.resetHourUtc` | no (0) | UTC hour at which the set changes |
| `settings.uniqueCategories` | no (false) | No two quests in a set share a category |
| `settings.hardestLast` | no (false) | The set goes from easy to hard; without `slotDifficulties` — exactly one quest of the highest difficulty |
| `settings.slotDifficulties` | no ([]) | Difficulty of each slot in the set, e.g. `[1, 2, 3]` |
| `quests[].id` | yes | Unique id; the game also derives the localization key from it |
| `quests[].weight` | no (1) | Selection weight. 0 disables the quest |
| `quests[].categories` | no ([]) | Categories for `uniqueCategories`. A quest without categories conflicts with nothing |
| `quests[].difficulty` | no (0) | Difficulty for `slotDifficulties` and `hardestLast` |
| `objectives[].statPath` | yes | Stat path, as in achievements (`grind`, `kill:*:grunt`) |
| `objectives[].statTarget` | yes | How much to reach, > 0 |
| `objectives[].mode` | no (`delta`) | `delta` — growth since the quest was issued; `absolute` — the stat value as is |
| `objectives[].id` | no (`o0`, `o1`…) | Only needed when there are several objectives and you want stable ids |
| `rewards[].key` / `amount` | yes / > 0 | What to grant. The game interprets the key (in Climber — a currency id) |

`delta` is meant for accumulating stats (counters). For Max stats (`score_best`) it means "beat your record by N", which is usually not what you want — for "score 10k in one run", add a stat the game resets at the start of a run and use `absolute`.

The config is checked by `DailyQuestValidator` at startup: errors and warnings go to the log, and a quest with an error is simply left out of the pool. A typo in `statPath` (stat not defined) is a warning: such a quest can never complete.

## Set rules

Without `uniqueCategories`, `hardestLast` and `slotDifficulties` the set is a plain weighted pick from the pool. With any of them it is a rule-based pick, and the set is ordered by difficulty, easy to hard.

- **`slotDifficulties`** sets the difficulty of each slot. Fewer slots than `dailyCount` — the rest can be any difficulty (with `hardestLast` — easier than the hardest quest in the pool). More slots — the hardest ones are used. Works without the other two flags.
- **`hardestLast`** without slots: the set has exactly one quest of the pool's highest difficulty; the others are easier.
- **`uniqueCategories`**: no two quests in the set share a category.

If a full set cannot be built under all the rules (not enough quests of the required difficulty or categories), the rules are relaxed one at a time:

1. without `uniqueCategories` — rewards depend on difficulty, so it matters more than variety;
2. without difficulty (`slotDifficulties` and `hardestLast`), but with `uniqueCategories`;
3. without any rules.

The player always gets as many quests as the pool allows — rules never produce an empty or truncated set. The search is bounded (`DailyQuestSelector.SearchBudget`), so a large pool with unsatisfiable rules will not hang the game.

The validator warns when the rules cannot be met by this pool: a slot asks for a difficulty no quest has; there are more slots than `dailyCount`; a full set under all rules cannot be built.

## Public API

| Type | What it is |
|---|---|
| `IDailyQuestService` / `DailyQuestService` | The service. `GetActive()`, `GetStatus(id)`, `ClaimAsync(id)`, `Tick()`, `ClaimableCount`, `TimeUntilReset`, event `Changed` |
| `IDailyQuestDebug` | Debug: `ForceComplete`, `SkipToNextPeriod`, `ResetState`. The same `DailyQuestService`, but a separate interface — game UI does not see it |
| `IDailyQuestProvider` | Where the config comes from. Ready-made: `ResourcesDailyQuestProvider`, `JsonDailyQuestProvider` |
| `IDailyQuestStateStore` | Where the state lives. `LoadAsync` returns `DailyQuestStateLoadResult`. Ready-made: `JsonDailyQuestStateStore(IPersistentStorage)` |
| `DailyQuestStateLoadResult` | Load result: `Loaded` (+ `State`), `Empty`, `Unreadable`, `Unavailable` |
| `IDailyQuestGameBridge` | `OnQuestsRotated`, `OnQuestCompleted`, `ApplyRewards` — the game grants the reward here. Required |
| `QuestStatus` | Quest snapshot for the UI: `Definition`, `State` (`InProgress`/`Completed`/`Claimed`), `ObjectiveProgresses`, `TotalProgress`, `CanClaim` |
| `QuestId`, `QuestObjectiveId` | Newtypes with `None`. The reward key is the shared `LongLeap.Core.RewardKey` |
| `DailyQuestJson` | JSON parsing → `DailyQuestConfig` |
| `DailyQuestValidator` | Config validation |
| `DailyQuestSelector` | Weighted, deterministic rule-based set selection |

## Initialization

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

`ResourcesDailyQuestProvider` reads a `TextAsset` through `Resources.Load` — the JSON is inside the build, no HTTP, so it also works on portals that repackage the build (GamePix). It reads once and caches the task: you can create it and call `LoadAsync()` early in loading (on the main thread) and hand it to the service later — the service gets the finished result. A missing file or broken JSON gives an empty pool and an error in the log.

A custom store returns a `DailyQuestStateLoadResult`: no save — `Empty()`, unreadable — `Unreadable()`, storage did not answer — `Unavailable()`. String parsing is ready-made: `JsonDailyQuestStateStore.Parse(json)`.

## Using it in the UI

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

void Update() => service.Tick();   // on reset a new set arrives together with Changed

async void OnClaimClicked(QuestId id) => await service.ClaimAsync(id);   // the reward arrives in bridge.ApplyRewards
```

`GetActive()` returns the same array and the same `QuestStatus` objects until something changes — no need to copy them, but don't modify them either. Reference comparison (`ReferenceEquals(old, status)`) is a cheap way to tell that a row does not need redrawing.

## How it works

- **The period changes lazily.** There is no timer: every `GetActive`/`ClaimAsync`/`Tick`/stat change checks whether a new period has started. An open quest UI calls `Tick()` from `Update` so the set changes in front of the player. Moving the clock backwards does not change the set.
- **What is saved is the starting point, not the progress.** When a quest is issued, the current stat value is stored (`baseline`); progress = stat − baseline. So progress survives restarts and nothing is written on every grind — writes happen only on issue, completion and claim.
- **A stat rolled back — the baseline drops.** If a `delta` stat falls below the baseline (the stats save is older than the quests save, or stats were reset), the baseline becomes the current value and is saved. Otherwise progress would sit at zero until the stat caught up with the old mark. What was gained before the rollback is not returned.
- **Completion latches — per objective.** A reached objective stays reached even if the stat later drops; the quest becomes `Completed` once all objectives are reached. So "10k in one run + 30 grinds" counts even if the grinds were finished in another run.
- **Claiming is crash-safe.** The quest is first marked as claimed, `ClaimAsync` waits for the write and only then calls `ApplyRewards`: at worst the player loses one reward, but never gets it twice. If the write fails — no reward, `false`, the player can press again. The bridge is required — without it there would be nowhere to grant the reward.
- **Writes are serialized.** Each save waits for the previous one, so an older state never overwrites a newer one.
- **The set is deterministic.** The seed is the period start; quests from the previous set are taken only if the set cannot be built without them.
- **The config changed mid-period.** Removed quests drop out of the set, new objectives count from the moment of loading, and if `dailyCount` grew, the set is topped up by the same rules: the remaining quests keep their slots and categories.
- **Storage did not answer** (`Unavailable`) — the service is off until restart, so a claimed reward is never issued again.
- **Save cannot be read** (`Unreadable`: corrupt or from a newer version) — no quests until the next reset: what was already claimed today is unknown, and a freshly issued set would match the old one.
- **Stat changes are cheap.** Only quests waiting for that stat are checked; nothing is allocated when nothing changes. `Changed` fires only if visible progress or state moved; `QuestStatus` is rebuilt lazily, on read. `GetActive()` also reconciles progress with the stats, so a stat changed without an event is visible immediately.

Internals: `ResetSchedule` (when a period starts), `ProgressEvaluator` (baseline, progress, latches, rollback) and `DailyQuestSelector` (set selection) — stateless, each tested separately; `DailyQuestService` ties them together.
