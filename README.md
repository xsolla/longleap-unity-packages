# LongLeap Packages
<https://gitea.longleap.games/Long_Leap_Games/Packages>

Shared Unity package library for LongLeap games: **22 UPM packages** in one Git
repo, consumed as a submodule at `Packages/LongLeap` and referenced from the host
project with `file:` paths.

Every package is embedded (local), so an Editor change is live at once — there is
no publish step.

This file is the single entry point: what every package is, how to add it, and how
to use it. Each released package also keeps its own `README.md` (full API tables,
internals) and `CONTEXT.md` (vocabulary and invariants) — follow the **Details**
link at the end of each package section when you need more.

Four packages are internal and not released; they are described separately in
[Internal packages](#internal-packages-not-released).

## Contents

- [Unity version](#unity-version)
- [Installing packages](#installing-packages)
- [Package map](#package-map)
- [Architecture](#architecture)
- [Typical startup order](#typical-startup-order)
- Packages
  - Foundation: [core](#comlongleapcore) · [localization](#comlongleaplocalization) · [tools](#comlongleaptools)
  - Features: [stats](#comlongleapstats) · [achievements](#comlongleapachievements) · [dailyrewards](#comlongleapdailyrewards) · [dailyquests](#comlongleapdailyquests) · [settings](#comlongleapsettings) · [analytics](#comlongleapanalytics) · [remoteconfig](#comlongleapremoteconfig)
  - Backends: [firebase](#comlongleapfirebase) · [nullproviders](#comlongleapnullproviders) · [gamepix](#comlongleapgamepix) · [backpack](#comlongleapbackpack)
  - Identity: [xsollaid](#comlongleapxsollaid)
  - Team kits: [bootstrap](#comlongleapbootstrap) · [ui](#comlongleapui)
  - Tooling: [buildmanager](#comlongleapbuildmanager)
- [Internal packages (not released)](#internal-packages-not-released): [skins](#comlongleapskins) · [skins.spine](#comlongleapskinsspine) · [customization](#comlongleapcustomization) · [devtools](#comlongleapdevtools)
- [Scripting defines](#scripting-defines)
- [Conventions](#conventions)
- [Working on the library](#working-on-the-library)
- [Where to look next](#where-to-look-next)

---

## Unity version

**Unity 6000.0 – 6000.4.** The floor is what every `package.json` declares
(`"unity": "6000.0"`). The ceiling comes from the **GamePix plugin**, not from the
packages themselves — **Unity 6000.5 requires updating the GamePix plugin** before
the project will build. Treat an Editor upgrade past 6000.4 as a GamePix task first.

---

## Installing packages

### 1. Get the submodule

```bash
git clone --recurse-submodules <project-url>
# or, in an existing clone
git submodule update --init --recursive
```

### 2. Declare the packages you need

The host project lists each package explicitly in `<project>/Packages/manifest.json`:

```json
"com.longleap.core": "file:LongLeap/com.longleap.core",
"com.longleap.stats": "file:LongLeap/com.longleap.stats",
```

A package present in the submodule but absent from that manifest is simply not
compiled. **Add every LongLeap package it depends on as well** — the `Depends on`
column in the [package map](#package-map) lists them. Unity packages
(Newtonsoft, Addressables, Localization, Input System…) are pulled in through the
`package.json` dependencies automatically.

The `file:` paths are relative to the project's `Packages/` folder, so they encode
where the submodule lives: `file:LongLeap/...` resolves only if the submodule sits
at `Packages/LongLeap`. **The location is a convention, not a requirement** — you
can mount it elsewhere, but then `.gitmodules` and every `file:` entry have to
change together.

> `manifest.json` at the repo root is **not** read by consumers — Unity only reads
> the host project's `Packages/manifest.json`.

### 3. Reference the assemblies

Runtime assemblies are auto-referenced, so code in `Assembly-CSharp` sees them
as soon as the package is in the manifest. A game with its own `.asmdef` files
must add them to that asmdef's references (e.g. `LongLeap.Stats`). Naming rule:
`com.longleap.stats` → `LongLeap.Stats`.

### 4. (Optional) Run the package tests

Unity runs package tests only for packages listed under `testables`:

```json
"testables": [ "com.longleap.core", "com.longleap.stats" ]
```

---

## Package map

**Foundation** — no dependency on other LongLeap packages

| Package | What it is |
|---|---|
| `com.longleap.core` | Contracts and domain types (auth, cloud saves, ads/portal, storage, experiments), the `CloudSaveService`, Unity storage implementations. Everything depends on this. |
| `com.longleap.localization` | `Loc` static facade over Unity Localization, `LocKey`, `LocalizedText` component. |
| `com.longleap.tools` | `DeviceInfo` device profile, `PageVisibility` (browser tab hidden/shown), `RuntimeUi` code-built UI bricks with a `RuntimeUiTheme`, WebGL `index.html` page mode. |

**Features**

| Package | What it is | Depends on |
|---|---|---|
| `com.longleap.stats` | Hierarchical stat tree (`kill:monk:grunt`), wildcard subscriptions, accumulate/max cascading, cloud-persisted. | core |
| `com.longleap.achievements` | Goals with objectives; progress derived from stats; cascading unlocks; hidden achievements. | core, stats |
| `com.longleap.dailyrewards` | Reward calendar: streaks, cycles (`Once`/`Loop`), grace period, UTC reset window. | core |
| `com.longleap.dailyquests` | JSON quest pool, weighted daily set per UTC reset window, progress from stats, rewards claimed by the player. | core, stats |
| `com.longleap.settings` | Typed settings (numeric / bool / enum / key binding) with per-group cloud persistence and a code-built settings panel. | core, localization, tools |
| `com.longleap.analytics` | GA4 Measurement Protocol: validated events, batching/retry, sessions, consent, A/B tagging, WebGL `sendBeacon` transport. | core |
| `com.longleap.remoteconfig` | Typed remote parameters with inline defaults, cache, throttling, deferred activation, A/B experiment info. | core |

**Backends** — provider implementations, chosen by game code at construction

| Package | What it is | Depends on | Gate |
|---|---|---|---|
| `com.longleap.firebase` | Anonymous auth, cloud saves, remote config, leaderboards. REST only, no Firebase SDK. | core, remoteconfig | Leaderboards: `XSOLLA_ID` |
| `com.longleap.nullproviders` | Null-object backends for the core seams (auth, cloud saves, ads, portal locale, leaderboards, leagues). | core | Leaderboards/leagues: `XSOLLA_ID` |
| `com.longleap.gamepix` | GamePix portal: ads, portal language, Xsolla ID via the portal, build tooling, HTML5 packaging. | core, xsollaid, buildmanager | `GAMEPIX` + plugin at `Assets/Plugins/GamePix` |
| `com.longleap.backpack` | Client for the LongLeap BackPack service: events that grant rewards, inventory items, achievement gating. | core, achievements, buildmanager | — |

**Identity**

| Package | What it is | Depends on | Gate |
|---|---|---|---|
| `com.longleap.xsollaid` | Xsolla ID sign-in (OAuth2/OIDC + PKCE), account widget, startup account flow with guest→account save merge, leaderboards and leagues. | core, localization, buildmanager | `XSOLLA_ID` for the `.Web` assembly |

**Team kits** — the plumbing and screens our games share; feature services are still built by the game

| Package | What it is | Depends on |
|---|---|---|
| `com.longleap.bootstrap` | Sequential loading pipeline of `ILoadingTask` steps, `ServiceContainer`, `GameServices` access point, optional Addressables loading, `LoadSceneTask`. | — (Addressables) |
| `com.longleap.ui` | Window system over Addressables, ready-made windows (settings, achievements, daily rewards, daily quests, backpack), priority-queued notifications. Each feature module compiles only when its feature package is installed. | tools (uGUI, Input System, Addressables) |

**Tooling**

| Package | What it is | Depends on | Gate |
|---|---|---|---|
| `com.longleap.buildmanager` | Editor window (**LongLeap / Build Manager**) that stages build options, applies them in one batch and dispatches the build. | — | Editor only |

**Internal** — in the repo for our own games, not released: `customization`,
`skins`, `skins.spine`, `devtools`. See
[Internal packages](#internal-packages-not-released).

---

## Architecture

Three words carry the whole design. They are defined in
`com.longleap.core/CONTEXT.md` and used consistently everywhere.

| Term | Meaning |
|---|---|
| **Service** | A feature module, defined by its interface (`ICloudSaveService`, `IStatSystem`). What game code talks to. |
| **Provider** | The interface a backend implements (`ICloudSaveProvider`, `IAuthProvider`). The swap seam. |
| **Backend** | A concrete provider implementation — Firebase, Null, GamePix, Xsolla. Game code never sees one directly. |

Each service is constructed on its own (`new CloudSaveService(provider, prefs)`).
The container that holds them (`ServiceContainer`) and the loading pipeline come
from `com.longleap.bootstrap`; which services to build, and in what order, is game
code. There is no `ServiceBuilder` — older docs mentioned one; it never existed.

Rules worth knowing before you add anything:

- **Contracts live in `core`, implementations live in backend packages.** A feature
  package never references a backend package; game code picks the backend when it
  constructs the service.
- **Null backends are opt-in, never a silent fallback.** They ship in `Runtime`
  (not `Tests`) and must be chosen explicitly at construction.
- **Name new services, providers and backends after what they are**, not with
  *Manager*, *Controller*, *System*, *Adapter*, *Stub* or *Mock* — each
  `CONTEXT.md` lists the rejected synonyms for its own vocabulary. A few older
  names predate the rule (`StatsSystem`, `AchievementSystem`,
  `UnityPlayerPrefsAdapter`) and are kept for compatibility.

---

## Typical startup order

How the packages fit together in a game's boot sequence. Each step is usually one
`LoadingTask` (see [bootstrap](#comlongleapbootstrap)):

1. **Localization** — `Loc.Init(...)`, so every later screen has texts.
2. **Remote config** — `RemoteConfigService` + `FetchAsync` with a short timeout.
3. **Analytics** — `AnalyticsService` with the experiment enricher from remote config.
4. **Identity** — Xsolla ID auth (`XsollaIdFactory.Create`), or the GamePix auth on the portal.
5. **Account gate + save switch** — decide guest/account *before* the save service starts.
6. **Cloud saves** — `CloudSaveService(provider, prefs)` → `InitializeAsync()`.
7. **Feature services on top of saves** — stats → achievements → daily rewards / daily quests → settings.
8. **UI** — `WindowService`, bind windows with `OnCreated<T>`, notifications.
9. **Scene** — `LoadSceneTask`.

---

# Packages

Every section follows the same order: **what it is**, **install**, **how to use**,
**pitfalls**, **details**.

---

## com.longleap.core

**What it is.** The foundation. Contracts other packages implement (auth, cloud
saves, ads, portal locale, storage, A/B experiments), the backend-agnostic
`CloudSaveService`, shared types (`RewardKey`, `ResetSchedule`) and Unity storage
implementations.

**Install.** `"com.longleap.core": "file:LongLeap/com.longleap.core"`. Assembly
`LongLeap.Core`. Needs `com.unity.nuget.newtonsoft-json` (pulled in automatically).

| Module | Namespace | Main types |
|---|---|---|
| Storage | `LongLeap.Core` | `IPlayerPrefs`, `IPersistentStorage`, `UnityPlayerPrefsAdapter`, `InMemoryPlayerPrefs`, `PlayerPrefsPersistentStorage`, `FilePersistentStorage` |
| Auth | `LongLeap.Core.Auth` | `IAuthProvider`, `AuthResult`, `UserId`, `IdToken` |
| Cloud saves | `LongLeap.Core.CloudSaves` | `ICloudSaveService`, `CloudSaveService`, `ICloudSaveProvider`, `CloudSaveKey`, `IAccountCloudSaveScope` |
| Ads / portal | `LongLeap.Core.Ads` | `IAdsProvider`, `AdResult`, `AdsDebug`, `IPortalLocale` |
| Experiments | `LongLeap.Core` | `IExperimentInfoProvider`, `AbExperimentInfo` |
| Shared types | `LongLeap.Core` | `RewardKey`, `ResetSchedule` |

**How to use.**

```csharp
using LongLeap.Core;
using LongLeap.Core.Ads;
using LongLeap.Core.Auth;
using LongLeap.Core.CloudSaves;

// Storage. PlayerPrefsPersistentStorage is WebGL-safe; FilePersistentStorage is not.
IPlayerPrefs prefs = new UnityPlayerPrefsAdapter();
IPersistentStorage storage = new PlayerPrefsPersistentStorage();

// Cloud saves: pick a provider (Firebase, Xsolla, Null), wrap it in the service.
var saves = new CloudSaveService(cloudSaveProvider, prefs);
await saves.InitializeAsync();                    // merges local and cloud, newer timestamp wins

var key = new CloudSaveKey("progress");
await saves.SetStringAsync(key, "42");            // local first, cloud push follows
string value = await saves.GetStringAsync(key);   // never waits for the network

// Auth
var result = await authProvider.LoginAsync();
if (result is AuthResult.Success s) { /* s.Token, s.Uid */ }

// Ads: Tick() every frame; the callback fires exactly once.
ads.ShowRewarded(r => { if (r == AdResult.Completed) GrantReward(); });
```

**Pitfalls.**
- `CloudSaveKey` throws on `. $ # [ ] /`.
- `FilePersistentStorage` runs on the thread pool — **not for WebGL**.
- Call `IAdsProvider.Tick()` every frame; gate unscaled-time code with `IsAdShowing`.
- `CloudSaveService.ResetLocalCacheAsync` (concrete class only, not on `ICloudSaveService`) forgets the local mirror; `DeleteAllAsync` also erases the cloud save.

**Details:** [`com.longleap.core/README.md`](com.longleap.core/README.md), [`CONTEXT.md`](com.longleap.core/CONTEXT.md).

---

## com.longleap.localization

**What it is.** Pluggable localization: a static `Loc` facade any code can call,
an `ILocalizationService` contract behind it, a Unity Localization string-table
implementation (`LocalizationService`) that never flashes raw keys while switching
locale, the serializable `LocKey`, and the `LocalizedText` component for TMP / uGUI
text.

**Install.** `"com.longleap.localization": "file:LongLeap/com.longleap.localization"`.
Assembly `LongLeap.Localization`. Pulls `com.unity.localization` 1.5.0, `com.unity.addressables` and `com.unity.ugui`.

**How to use.**

1. Create Unity Localization assets: `LocalizationSettings`, `Locale`s, a `StringTableCollection`.
2. Write a small bootstrapper (the package has none):

```csharp
public class LocalizationBootstrapper : MonoBehaviour
{
    [SerializeField] private LocalizedStringTable[] _tables;
    public LocalizationService Service { get; private set; }

    IEnumerator Start()
    {
        yield return LocalizationSettings.InitializationOperation;
        var preloaded = new StringTable[_tables.Length];
        yield return LocalizationService.LoadTables(_tables, preloaded);

        Service = new LocalizationService(_tables, preloaded);
        Service.SetCoroutineHost(this);   // needed for reloads on locale change
        Loc.Init(Service);
    }

    void OnDestroy()
    {
        if (Service == null) return;
        Loc.Reset(Service);
        Service.Dispose();
    }
}
```

3. Use it:

```csharp
string text  = Loc.Resolve("welcome_message");
string label = Loc.ResolveFormat("player_level", 5);   // "Level {0}" → "Level 5"
service.SwitchLanguage("ru");                            // Loc.OnLanguageChanged fires when ready
```

In prefabs, add `LocalizedText` next to a `TMP_Text` and set **Key** in the Inspector.

A game without Unity Localization tables can pass any `ILocalizationService` (e.g. a
dictionary) to `Loc.Init`.

**Pitfalls.**
- Without `Loc.Init`, `Loc.Resolve` returns the key itself — buttons will read `btn_xsolla_sign_in`.
- Formatting uses `string.Format` (`{0}`); Smart Strings are not applied.
- Without an active coroutine host a locale change drops all tables.

**Details:** [`com.longleap.localization/README.md`](com.longleap.localization/README.md), [`CONTEXT.md`](com.longleap.localization/CONTEXT.md).

---

## com.longleap.tools

**What it is.** Small shared tools every game uses:
- `DeviceInfo` — the single place that decides the device profile (`Desktop`, `Mobile`, `WebDesktop`, `WebMobile`);
- `PageVisibility` — browser tab hidden/shown, the reliable "player left" signal on WebGL;
- `RuntimeUi` + `RuntimeUiTheme` — code-built UI bricks used by the settings panel and the `ui` windows;
- **WebGL page mode** (editor) — fixed / free aspect, orientation overlay, mobile pixel ratio for `index.html`.

**Install.** `"com.longleap.tools": "file:LongLeap/com.longleap.tools"`. Assemblies:
`LongLeap.Tools` (device profile, page visibility), `LongLeap.Tools.UI` (RuntimeUi),
`LongLeap.Tools.Editor`.

**How to use.**

```csharp
using LongLeap.Tools;

if (DeviceInfo.IsMobile) { /* touch controls */ }
switch (DeviceInfo.Profile) { case DeviceProfile.WebMobile: /* ... */ break; }

PageVisibility.Changed += hidden => { if (hidden) Save(); };
```

- **Theme:** *Create ▸ LongLeap ▸ Runtime UI Theme*, name it `RuntimeUiTheme`, put it in any `Resources` folder (or set `RuntimeUi.Theme` from code). Set its **Font** — without a font, code-built text is disabled.
- **Test another profile:** menu **Tools ▸ Device Profile**, or `DeviceInfo.Override = DeviceProfile.WebMobile;` in code.
- **WebGL page:** menu **Tools ▸ WebGL Page Mode** (Fixed / Free Aspect, Orientation, Mobile Pixel Ratio 1). Stored in `ProjectSettings/LongLeapWebGlPage.asset` — commit it.

**Pitfalls.**
- `PageVisibility` only fires in WebGL players; keep `OnApplicationPause` for native mobile.
- Don't check `Application.isMobilePlatform` / `UNITY_WEBGL` in game code — ask `DeviceInfo`.

**Details:** [`com.longleap.tools/README.md`](com.longleap.tools/README.md), [`CONTEXT.md`](com.longleap.tools/CONTEXT.md).

---

## com.longleap.stats

**What it is.** A hierarchical stat system. Paths are colon-delimited
(`kill:monk:grunt`); branches aggregate their children (`Accumulate` = sum,
`Max` = max); subscriptions accept `*` wildcards. Leaf values are saved one by
one through an `IStatPersistence` the game provides (branches are recalculated on
load). Achievements and daily quests derive progress from it.

**Install.** `"com.longleap.stats": "file:LongLeap/com.longleap.stats"` (+ core).
Assembly `LongLeap.Stats`.

**How to use.**

```csharp
var stats = new StatsSystem();

// Parents before children.
stats.Define("kill", StatType.Accumulate);
stats.Define("kill:monk", StatType.Accumulate);
stats.Define("kill:monk:grunt", StatType.Value);
stats.Define("bighit", StatType.Max);
stats.Define("bighit:monk", StatType.MaxValue);

IStatPersistence persistence = new CloudSaveStatPersistence(cloudSave);   // yours, see below
await stats.LoadAsync(persistence);

stats.Add("kill:monk:grunt", 1);        // parents recalculate automatically
stats.Set("bighit:monk", 50);           // MaxValue keeps max(current, 50)

stats.Subscribe("kill:*:grunt", (path, value) => { /* ... */ });
long allKills = stats.GetAggregate("kill");

await stats.SaveAsync(persistence);     // writes only leaves changed since the last save
```

The package ships no persistence; a cloud-save one is a few lines:

```csharp
public sealed class CloudSaveStatPersistence : IStatPersistence
{
    private readonly ICloudSaveService _saves;
    public CloudSaveStatPersistence(ICloudSaveService saves) => _saves = saves;

    public Task SaveAsync(StatPath path, long value) =>
        _saves.SetStringAsync(Key(path), value.ToString(CultureInfo.InvariantCulture));

    public async Task<StatValue> LoadAsync(StatPath path)
    {
        string raw = await _saves.GetStringAsync(Key(path));
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)
            ? StatValue.Of(v) : StatValue.None;
    }

    private static CloudSaveKey Key(StatPath path) => new CloudSaveKey("stat_" + path.Value);
}
```

Colons in stat paths are valid in a `CloudSaveKey`, so nested stats need no renaming.

**Pitfalls.**
- **No auto-save** — call `SaveAsync` yourself (timer, pause, quit). `Task.Delay` does not work on WebGL.
- Subscription callbacks run inside a lock — keep them fast.
- Define every parent before its children.

**Details:** [`com.longleap.stats/README.md`](com.longleap.stats/README.md), [`CONTEXT.md`](com.longleap.stats/CONTEXT.md).

---

## com.longleap.achievements

**What it is.** Achievements with objectives. An objective is either
**stat-driven** (`statPath` + `statTarget`) or **dependency-based** (`required`
achievements). Progress is derived live from `IStatSystem`; only the unlock flag
and time are saved. Unlocks cascade through dependents; hidden achievements are
supported; a validator catches config mistakes.

**Install.** `"com.longleap.achievements": "file:LongLeap/com.longleap.achievements"`
(+ core, stats). Assembly `LongLeap.Achievements`.

**How to use.**

1. Put definitions in `Resources/Achievements/`: a `manifest.json` listing the files, and the files.

   ```json
   // manifest.json
   { "files": ["combat.json"] }

   // combat.json
   { "achievements": [
       { "id": "kill_100", "group": "combat",
         "objectives": [ { "id": "kc_100", "statPath": "kill", "statTarget": 100 } ] } ] }
   ```

2. Implement `IAchievementStateStore` (load / mark unlocked / delete all) over your save, and optionally `IAchievementGameBridge` (`OnAvailable`, `OnUnlocked` — popups, rewards).
3. Initialize:

   ```csharp
   var achievements = new AchievementSystem();
   await achievements.Initialize(
       provider:   new ResourcesAchievementProvider(),   // Resources/Achievements/
       stateStore: myStateStore,
       gameBridge: myBridge,                            // optional
       stats:      statsSystem);                        // optional, needed for stat objectives

   foreach (var group in achievements.GetGroups())
       foreach (var status in achievements.GetByGroup(group)) { /* status.IsUnlocked, ObjectiveProgresses */ }
   ```

**Pitfalls.**
- A missing/broken `manifest.json` is logged as an **error**: an empty set silently ungates everything gated by achievements — treat it as a broken build.
- Unlocks are permanent; shown progress can go down if a stat drops.
- `ResetAll()` does not reset stats — achievements whose goals are met unlock again immediately.

**Details:** [`com.longleap.achievements/README.md`](com.longleap.achievements/README.md), [`CONTEXT.md`](com.longleap.achievements/CONTEXT.md).

---

## com.longleap.dailyrewards

**What it is.** A daily reward calendar: one claim per UTC reset window, streak
tracking with a configurable grace period, cycles that run `Once` or `Loop`, a
calendar view-model for the UI, and crash-safe "save, then grant" claiming.

**Install.** `"com.longleap.dailyrewards": "file:LongLeap/com.longleap.dailyrewards"`
(+ core 0.2.0+). Assemblies to reference: `LongLeap.DailyRewards`, `LongLeap.Core`.

**How to use.**

1. Create the config: *Assets ▸ Create ▸ LongLeap ▸ Daily Rewards ▸ Config*. In the Inspector fill the days (each: reward key such as `soft` + amount), the cycle policy (`Once` / `Loop`), the reset hour (UTC) and the grace period.
2. Pick a state store: `new JsonDailyRewardsStateStore(persistentStorage)`, or your own over cloud saves that reuses `JsonDailyRewardsStateStore.Parse(json)`.
3. Implement the bridge — its only job is granting the reward:

   ```csharp
   public sealed class WalletBridge : IDailyRewardsGameBridge
   {
       public void ApplyReward(DailyRewardClaimResult result)
       {
           foreach (var entry in result.Day.Rewards)
               wallet.Add(entry.Key, entry.Amount);
       }
   }
   ```

4. Bring it up and register only when ready:

   ```csharp
   var service = new DailyRewardsService(config, store, bridge);
   await service.InitializeAsync();
   if (service.IsReady)
   {
       services.Register<IDailyRewardsService>(service);
       services.Register<IDailyRewardsDebug>(service);   // dev tools only
   }
   ```

5. UI: use `DailyRewardsScreen` from `com.longleap.ui` (`screen.Bind(service, sounds, rewardIcon)`), or your own — draw `GetCalendar()`, call `await ClaimAsync()`, redraw on `Changed`, call `Tick()` from `Update` while open, show `TimeUntilNextClaim`.

**Pitfalls.**
- A custom store must return `DailyRewardsStateLoadResult.Unavailable()` when storage throws — **never `Empty`**, or a claimed day can be granted twice.
- `IsReady == false` → hide the rewards entry point.
- `ClaimAsync` returns `false` when the save failed; nothing was granted, and the day stays claimable.

**Details:** [`com.longleap.dailyrewards/README.md`](com.longleap.dailyrewards/README.md), [`CONTEXT.md`](com.longleap.dailyrewards/CONTEXT.md).

---

## com.longleap.dailyquests

**What it is.** Daily quests in the style of achievements: quests are described in
JSON, progress comes from `IStatSystem`, state is stored through
`IDailyQuestStateStore`. The differences from achievements:
- quests are **issued as a set per period** (UTC reset to reset), picked from a weighted pool;
- progress counts **from the moment of issue** (`delta`) by default, not lifetime;
- rewards are **claimed by the player** (`ClaimAsync`), not granted automatically.

**Install.** `"com.longleap.dailyquests": "file:LongLeap/com.longleap.dailyquests"`
(+ core 0.2.0+, stats). Assembly `LongLeap.DailyQuests`.

**How to use.**

1. Add `Resources/DailyQuests/daily_quests.json`. Adding a quest is adding a JSON object — no code:

   ```json
   {
     "settings": { "dailyCount": 3, "resetHourUtc": 0 },
     "quests": [
       { "id": "grind_50", "weight": 1,
         "objectives": [ { "statPath": "grind", "statTarget": 50 } ],
         "rewards":    [ { "key": "soft", "amount": 5 } ] }
     ]
   }
   ```

   Optional set rules: `uniqueCategories`, `hardestLast`, `slotDifficulties`; per quest: `categories`, `difficulty`, objective `mode` (`delta` / `absolute`).

2. Implement `IDailyQuestGameBridge` (`OnQuestsRotated`, `OnQuestCompleted`, `ApplyRewards` — grant rewards here; required).
3. Initialize and register:

   ```csharp
   var quests = new DailyQuestService(
       provider: new ResourcesDailyQuestProvider(),
       store:    new JsonDailyQuestStateStore(persistentStorage),
       bridge:   myBridge,
       stats:    statsSystem);
   await quests.InitializeAsync();

   services.Register<IDailyQuestService>(quests);
   services.Register<IDailyQuestDebug>(quests);   // dev tools only
   ```

4. UI: redraw on `Changed` from `GetActive()` (each `QuestStatus` has `TotalProgress`, `CanClaim`); call `Tick()` from `Update` while the panel is open; `await ClaimAsync(id)`. Or use `DailyQuestPanel` from `com.longleap.ui`.

**Pitfalls.**
- `delta` is meant for counters. For "score 10k in one run" use a stat the game resets at run start and `absolute` mode.
- A typo in `statPath` is only a validator **warning** — that quest can never complete.
- A store that cannot answer must return `Unavailable()`; the service then stays off until restart, so claimed rewards can't be granted twice.

**Details:** [`com.longleap.dailyquests/README.md`](com.longleap.dailyquests/README.md), [`CONTEXT.md`](com.longleap.dailyquests/CONTEXT.md).

---

## com.longleap.settings

**What it is.** Typed settings defined in code as groups (`NumericSetting<float/int>`,
`Setting<bool>`, `Setting<Key>` key bindings, `EnumSetting<T>`). One cloud-save
string per group. `SettingsPanel` builds the settings UI in code from a
`SettingsWindowConfig` asset, picked per device profile.

**Install.** `"com.longleap.settings": "file:LongLeap/com.longleap.settings"` (+ core,
localization, tools). Assemblies: `LongLeap.Settings` (model, service),
`LongLeap.Settings.UI` (panel).

**How to use.**

1. Define a group:

   ```csharp
   public sealed class AudioSettings : ISettingsGroup
   {
       public string StorageKey => "settings.audio";
       public string DisplayNameKey => "settings_audio";

       public NumericSetting<float> Music { get; } = new("audio.music_volume", 1f, 0f, 1f, "settings_music_volume");
       public Setting<bool> Vibration { get; } = new("gameplay.vibration", true, "gameplay_vibration");

       public Setting[] All => new Setting[] { Music, Vibration };

       public string Serialize() => JsonConvert.SerializeObject(new { music = Music.Value, vibration = Vibration.Value });
       public void Deserialize(string json)
       {
           var d = JsonConvert.DeserializeAnonymousType(json, new { music = 1f, vibration = true });
           if (d != null) { Music.Value = d.music; Vibration.Value = d.vibration; }
       }
   }
   ```

2. Create the service:

   ```csharp
   var settings = new SettingsService(cloudSave);   // null cloudSave = settings live for one session
   settings.Register(new AudioSettings());
   await settings.LoadAllAsync();
   settings.OnChanged += s => { /* react */ };
   settings.SetValue(audio.Music, 0.5f);            // always through the service
   await settings.SaveAllAsync();                    // e.g. when the settings window closes
   ```

3. UI: create *Create ▸ LongLeap ▸ Settings Window Config*, add sections and rows (pick `SettingKey` from the dropdown). Use `SettingsWindow` from `com.longleap.ui`, or host the panel yourself:

   ```csharp
   _panel = new SettingsPanel(root, configSet, onBack: Close);
   _panel.Bind(settings);
   ```

**Pitfalls.**
- Set values through `ISettingsService.SetValue`, not `Setting<T>.Value` — only the service clamps and fires `OnChanged`.
- The panel **never saves**; call `SaveAllAsync` on close.
- `SettingsPanel` has no row for `EnumSetting` yet.
- Enum labels need localization keys `{LabelKey}_{enumname}` (e.g. `lang_en`).

**Details:** [`com.longleap.settings/README.md`](com.longleap.settings/README.md), [`CONTEXT.md`](com.longleap.settings/CONTEXT.md).

---

## com.longleap.analytics

**What it is.** Game analytics sent to Google Analytics 4 through the Measurement
Protocol, with no Firebase or gtag SDK. It validates events against GA4 rules,
batches with retry/backoff and a bounded queue, tracks sessions and engagement
time, sends device info, supports consent, `user_id`, user properties and A/B
experiment tags. On WebGL it delivers through `navigator.sendBeacon`, which
survives tab close.

**Install.** `"com.longleap.analytics": "file:LongLeap/com.longleap.analytics"` (+ core).
Assembly `LongLeap.Analytics`.

**How to use.**

1. In GA4: **Admin ▸ Data streams** → copy the Measurement ID; **Measurement Protocol API secrets ▸ Create** → copy the secret.
2. In Unity: *Assets ▸ Create ▸ LongLeap Services ▸ GA4 Config*, fill in both.
3. Wire it up:

   ```csharp
   IAnalyticsBackend backend = ga4Config.IsConfigured
       ? new Ga4Backend(ga4Config.ToSettings(),
                        AnalyticsClientId.GetOrCreate(new UnityPlayerPrefsAdapter()),
                        WebGlBeaconTransport.CreateForCurrentPlatform(ga4Config.ToSettings()))
         { Device = Ga4DeviceInfo.FromCurrentPlatform() }
       : new ConsoleAnalyticsBackend();

   var analytics = new AnalyticsService(backend, new AnalyticsOptions { CollectionEnabled = playerConsented });
   analytics.AddEnricher(new ExperimentEnricher(remoteConfig));   // optional A/B tags
   AnalyticsFlushDriver.Attach(analytics);                         // timer, pause, quit
   WebGlPageLifecycle.Attach(analytics);                           // WebGL: flush on tab hide

   analytics.Send(new AnalyticsEvent("tutorial_completed").With("step_count", 7));
   ```

4. For events sent from several places, make a typed class deriving `AnalyticsEventBase`.

**Pitfalls.**
- GA4 answers `2xx` even for events it discards — read the warnings the package logs.
- Names: letter first, `[A-Za-z0-9_]`, ≤ 40 chars, no reserved names/prefixes.
- The API secret ships in the build; treat it as public.
- On the GamePix portal, start GA4 only under `GAMEPIX_ALLOW_ANALYTICS`.
- Use `ValidateOnly` in settings to get GA4 validation messages while debugging.

**Details:** [`com.longleap.analytics/README.md`](com.longleap.analytics/README.md), [`CONTEXT.md`](com.longleap.analytics/CONTEXT.md).

---

## com.longleap.remoteconfig

**What it is.** Typed remote game parameters with inline defaults. The last
successful fetch is cached in `IPlayerPrefs`, so a launch starts with the previous
session's values. It shares in-flight fetches, supports cancellation, throttling
and deferred activation, and exposes the player's A/B experiments for analytics.
Backend-agnostic: the Firebase provider lives in `com.longleap.firebase`.

**Install.** `"com.longleap.remoteconfig": "file:LongLeap/com.longleap.remoteconfig"`
(+ core). Assembly `LongLeap.RemoteConfig`.

**How to use.**

```csharp
IPlayerPrefs prefs = new UnityPlayerPrefsAdapter();
IRemoteConfigProvider provider = new FirebaseRemoteConfigProvider(firebaseConfig, prefs);
// or new NullRemoteConfigProvider() for offline builds and tests

var remoteConfig = new RemoteConfigService(provider, prefs);
var status = await remoteConfig.FetchAsync(token);   // Success / Failure / Throttled / Canceled

int maxEnemies = remoteConfig.GetInt("max_enemies", 10);    // default if missing or invalid
bool newShop   = remoteConfig.GetBool("new_shop", false);
WaveConfig w   = remoteConfig.GetJson("wave_1", WaveConfig.Default);
```

To avoid values changing mid-level, pass `new RemoteConfigOptions { ActivateOnFetch = false }`
and call `remoteConfig.Activate()` at a safe moment.

**Pitfalls.**
- `CancellationTokenSource(TimeSpan)` does not fire on WebGL (no threads) — cancel the startup timeout from a coroutine or the player loop.
- Use a separate `CacheKey` per Firebase project / environment.
- Main thread only. Dispose the service to abort a fetch in flight.

**Details:** [`com.longleap.remoteconfig/README.md`](com.longleap.remoteconfig/README.md), [`CONTEXT.md`](com.longleap.remoteconfig/CONTEXT.md).

---

## com.longleap.firebase

**What it is.** Production Firebase backends over REST (no Firebase SDK, so it
works on WebGL): anonymous auth (`FirebaseAnonymousAuthProvider`), Realtime
Database cloud saves with guest and account scopes (`FirebaseCloudSaveProvider`),
Remote Config with A/B info (`FirebaseRemoteConfigProvider`) and leaderboards
(`FirebaseLeaderboardProvider`, only with `XSOLLA_ID`).

**Install.** `"com.longleap.firebase": "file:LongLeap/com.longleap.firebase"` (+ core,
remoteconfig). Assembly `LongLeap.Firebase`; leaderboards in
`LongLeap.Firebase.Leaderboards` (compiles only with `XSOLLA_ID` and `com.longleap.xsollaid` installed — an optional dependency).

**How to use.**

1. Firebase console: enable **Authentication ▸ Anonymous**, create a **Realtime Database**, apply rules (template in the package README — leaderboards need `".indexOn": ["negScore"]`), publish **Remote Config** parameters if used.
2. Create *Assets ▸ Create ▸ LongLeap Services ▸ Firebase Config* at `Resources/Backend/FirebaseConfig.asset`; fill `ApiKey`, `DatabaseUrl`, `GoogleAppId`, `ProjectNumber`.
3. Construct — share one `auth` and one `http` between providers:

   ```csharp
   var config = Resources.Load<FirebaseConfigSO>("Backend/FirebaseConfig");
   var auth = new FirebaseAnonymousAuthProvider(config.ApiKey);
   var http = new FirebaseHttpClient(config.DatabaseUrl, auth);

   var saves = new CloudSaveService(new FirebaseCloudSaveProvider(http, auth, prefs), prefs);
   var remoteConfig = new RemoteConfigService(new FirebaseRemoteConfigProvider(config, prefs), prefs);
   ```

**Pitfalls.**
- Without the `.indexOn` rule every leaderboard read fails with "Index not defined".
- If a token refresh fails, a **new** anonymous user is created — the old guest save becomes unreachable.
- Failures look like empty data (`null` / `false` / empty dictionary).
- Account saves are protected only by an anonymous token; leaderboard integrity is client-side.

**Details:** [`com.longleap.firebase/README.md`](com.longleap.firebase/README.md), [`CONTEXT.md`](com.longleap.firebase/CONTEXT.md).

---

## com.longleap.nullproviders

**What it is.** Null-object backends for the core seams. Each does nothing, makes
no network calls and completes synchronously with a safe default, so the services
on top need no null checks. For guest play, offline builds, builds without ads,
and tests.

| Type | Implements |
|---|---|
| `NullAuthProvider` | `IAuthProvider` — succeeds with a fixed user id (`"anonymous"` or your own) |
| `NullCloudSaveProvider` | `ICloudSaveProvider`, `IAccountCloudSaveScope` — writes dropped, reads empty; data lives only in `CloudSaveService`'s local cache |
| `NullAdsProvider` | `IAdsProvider` — never available; callbacks fire once with `AdResult.Unavailable` |
| `NullPortalLocale` | `IPortalLocale` — always `false`, the game's own language applies |
| `NullLeaderboardProvider`, `NullLeagueProvider` | Leaderboards / leagues (assembly `LongLeap.Null.XsollaId`, only with `XSOLLA_ID`) |

**Install.** `"com.longleap.nullproviders": "file:LongLeap/com.longleap.nullproviders"`
(+ core). Assembly `LongLeap.Null`, namespace `LongLeap.Null`.

**How to use.** Choose it explicitly at construction:

```csharp
ICloudSaveProvider provider = isSignedIn ? new FirebaseCloudSaveProvider(http, auth, prefs)
                                         : new NullCloudSaveProvider();
IAdsProvider ads = isPortalBuild ? new GamePixAdsProvider() : new NullAdsProvider();
```

Null backends for other seams live next to their interfaces: `NullRemoteConfigProvider`
(remoteconfig), `NullAnalyticsBackend` (analytics), `NullBackPackService` (backpack),
`Null…Service` (customization).

**Pitfalls.**
- Never a silent fallback for a real backend that failed.
- Never use `NullCloudSaveProvider` for a signed-in account — the save switch would re-merge guest data on every launch.
- Every default `NullAuthProvider` reports the same user; pass a per-device id when that matters.

**Details:** [`com.longleap.nullproviders/README.md`](com.longleap.nullproviders/README.md), [`CONTEXT.md`](com.longleap.nullproviders/CONTEXT.md).

---

## com.longleap.gamepix

**What it is.** The backend for the GamePix web portal. It wraps the vendor GamePix
Unity plugin behind LongLeap seams — `GamePixAdsProvider` (`IAdsProvider`:
interstitial, rewarded, score), `GamePixPortalLocale` (`IPortalLocale`), Xsolla ID
through the portal (`XsollaIdAuthGamePix` / `XsollaIdAuthJS`) — and adds the editor
tooling a portal build needs (`index.html` postprocess, HTML5 zip packager, Build
Manager target).

**Install.** `"com.longleap.gamepix": "file:LongLeap/com.longleap.gamepix"` (+ core,
xsollaid, buildmanager). WebGL only.

**How to use.**

1. Import the vendor plugin `.unitypackage`; it installs into **`Assets/Plugins/GamePix`**. Keep it there — the vendor code is hard-wired to that path, so it cannot ship inside this package.
2. Nothing to do for the plugin's asmdefs: every plugin import strips their `GAMEPIX` constraint, and the package puts it back automatically right after the import (logged in the console). **GamePix ▸ Required ▸ Plugin Has defineConstraints** does the same by hand, e.g. if the plugin was updated before this package was added.
3. **GamePix ▸ Required ▸ Plugin Enabled (GAMEPIX)**.
4. If the plugin has no `Assets/Plugins/GamePix/User` folder, also enable **GamePix ▸ Optional ▸ Xsolla ID via JS Bridge (GAMEPIX_JS)**.
5. In code:

   ```csharp
   #if GAMEPIX
   IAdsProvider ads = new LongLeap.GamePix.GamePixAdsProvider();
   IPortalLocale locale = new LongLeap.GamePix.GamePixPortalLocale();
   #if GAMEPIX_JS
   IXsollaIdAuth auth = new LongLeap.GamePix.XsollaIdAuthJS();
   #else
   IXsollaIdAuth auth = new LongLeap.GamePix.XsollaIdAuthGamePix();
   #endif
   #endif

   void Update() => ads.Tick();   // required every frame
   ```

6. Build from **LongLeap ▸ Build Manager** (target "GamePix: …"). For an HTML5 upload, use **GamePix ▸ Package HTML5 Zip...** on a built WebGL folder.

**Pitfalls.**
- The runtime assembly exists only with `GAMEPIX`.
- `XsollaIdAuthJS` waits with `Task.Delay`, which can hang in WebGL — prefer `XsollaIdAuthGamePix`.
- `GAMEPIX_AD_DEBUG` must be off before portal review.
- The portal has no sign-out (`CanSignOut == false`).

**Details:** [`com.longleap.gamepix/README.md`](com.longleap.gamepix/README.md), [`CONTEXT.md`](com.longleap.gamepix/CONTEXT.md).

---

## com.longleap.backpack

**What it is.** A client for the LongLeap BackPack service. It sends player events
that can grant inventory rewards, reads and spends items in the player's backpack,
and gates achievement persistence on server acceptance
(`BackPackAchievementStateStore`).

**Install.** `"com.longleap.backpack": "file:LongLeap/com.longleap.backpack"` (+ core,
achievements, buildmanager). Assembly `LongLeap.Backpack`.

**How to use.**

1. **BackPack ▸ Select Config Asset** — creates/selects `Assets/Resources/Backend/BackPackConfig.asset` (exact path). Fill game ID and endpoints per environment.
2. **BackPack ▸ Required ▸ Environment ▸ Local / Development / Production** (also in Build Manager).
3. Create the service and feed it the signed-in player's Xsolla ID:

   ```csharp
   var config = Resources.Load<BackPackConfigSO>(BackPackConfigSO.ResourcePath);
   IBackPackService backPack = config != null
       ? new BackPackService(() => BackPackIdentity.XsollaId, config.Active)
       : NullBackPackService.Instance;

   BackPackIdentity.SetXsollaId(xsollaId);   // after sign-in; Clear() on sign-out

   var reply = await backPack.SendEventAsync("tutorial_completed");
   if (reply.RewardGranted) Debug.Log(reply.RewardId);

   var rewards = await backPack.GetRewardsAsync();
   var del = await backPack.DeleteItemAsync(item.Id);
   if (del.IsDeleted) GrantInGame(item.Rewards);   // safe to grant only on IsDeleted
   ```

4. Achievements: wrap your store — `new BackPackAchievementStateStore(existingStore, backPack)`; unlocks persist only after BackPack accepts the event.

**Pitfalls.**
- Request methods never throw on network/server errors (only `OperationCanceledException`); check `Error`.
- Don't cancel `DeleteItemAsync` once sent.
- An empty base URL is a valid "off" switch. Don't ship a build configured for Local.
- `BackPackDebugApi` is destructive and exists only in Editor / development builds.

**Details:** [`com.longleap.backpack/README.md`](com.longleap.backpack/README.md), [`CONTEXT.md`](com.longleap.backpack/CONTEXT.md).

---

## com.longleap.xsollaid

**What it is.** Xsolla ID sign-in — OAuth2/OIDC authorization code + PKCE —
self-contained (nothing to copy into `Assets/`: the jslib and callback page ship in
the package). It also contains:
- `LongLeap.XsollaId.UI` — a drop-in account widget prefab (sign-in button / avatar card);
- `LongLeap.XsollaId.Accounts` — the startup account flow: account or guest before the save service starts, guest progress merged into the account by per-key rules;
- leaderboards and leagues (`LongLeap.XsollaId.Leaderboards` / `.Leagues`, in the `XSOLLA_ID`-gated `.Web` assembly).

**Install.** `"com.longleap.xsollaid": "file:LongLeap/com.longleap.xsollaid"` (+ core,
localization, buildmanager). Assemblies: `LongLeap.XsollaId` (contracts),
`LongLeap.XsollaId.Web` (HTTP, browser, leaderboards; **only with `XSOLLA_ID`**),
`LongLeap.XsollaId.UI`, `LongLeap.XsollaId.Accounts`.

### Setup

Sign-in needs the define **and** a config asset. Either one missing and sign-in
reports `Unavailable`, the button stays disabled, and the widget hides itself — it
is not a compile error, so this is the first place to look when the account block
"did not appear".

1. **Define:** **Xsolla ID ▸ Required ▸ Sign-in Enabled (XSOLLA_ID)**. Without it, not a single request reaches `id.xsolla.com`.
2. **Config:** *Assets ▸ Create ▸ LongLeap Services ▸ Xsolla ID Config* at exactly `Assets/Resources/Backend/XsollaIdConfig.asset` (jump there with **Xsolla ID ▸ Select Config Asset**). Everything in it is a public identifier — commit it; there is no client secret.
3. **Environment:** **Xsolla ID ▸ Required ▸ Environment ▸ Stage | Production**. They are two separate client registrations, each with its own `clientId` and redirect URIs.

| Field | Stage default | Production default |
|---|---|---|
| `issuerBaseUrl` | `https://id-staging.xsolla.com` | `https://id.xsolla.com` |
| `clientId` | *(empty — required)* | *(empty — required)* |
| `redirectUri` | `http://127.0.0.1:8081/xid-callback.html` | *(empty — required)* |

Shared: `scopes` (default `openid offline email profile` — **`offline`, not
`offline_access`**, or refresh can fail with `invalid_scope`) and
`allowedGameOrigins` (checked before the window opens; default `http://127.0.0.1:8081` for local testing — add your real game origins, or empty the list to skip the check). Build
Manager blocks the build when `XSOLLA_ID` is on and `clientId` / `redirectUri` is
empty.

**Redirect URI.** The server compares it character for character. For our own
hosting it is **derived at runtime** from the page URL
(`origin + page directory + xid-callback.html`), so subfolders and different hosts
work; the asset's `redirectUri` is only the fallback (Editor, standalone). The
callback page is placed at the build root by `XsollaIdCallbackPagePostprocessor`.
`127.0.0.1` and `localhost` are different origins.

### Init and sign-in

One `#if` for the whole game — everything downstream sees interfaces only:

```csharp
IXsollaIdAuth auth;
IXsollaIdAvatarLoader avatars;

#if XSOLLA_ID
var config = Resources.Load<XsollaIdConfigSO>("Backend/XsollaIdConfig");
auth    = XsollaIdFactory.Create(config, new UnityPlayerPrefsAdapter(), deriveRedirectFromPage: true);
avatars = XsollaIdFactory.CreateAvatarLoader();
#else
auth    = new UnavailableXsollaIdAuth();
avatars = new UnavailableXsollaIdAvatarLoader();
#endif

if (!Loc.IsReady) Loc.Init(myLocalizationService);   // labels first

// Bind before checking IsAvailable, or the widget sits in "checking" forever.
FindAnyObjectByType<XsollaIdAccountWidget>(FindObjectsInactive.Include)?.Bind(auth, avatars);

if (!auth.IsAvailable) return;
auth.SignedIn  += OnSignedIn;
auth.SignedOut += OnSignedOut;
_ = auth.RestoreAsync();   // silent restore from the refresh token; don't await at boot
```

```csharp
async void OnSignInClicked()   // call synchronously from onClick, or the popup is blocked
{
    var result = await _auth.SignInAsync();
    if (result is XsollaIdResult.Success s) Debug.Log(s.Session.ShortDisplayName);
    else if (result is XsollaIdResult.Failure f) Debug.LogWarning($"{f.Code}: {f.Message}");
}
```

API guarantees: nothing throws (failures arrive as `XsollaIdResult.Failure`);
`Create` returns a stub instead of an error when the config is incomplete, so
`IsAvailable` is the only check UI needs; `SignInAsync` is re-entrant-safe; read
`Current` / `IsSignedIn` only once `IsReady`. `IXsollaIdAuth` is **not**
`IAuthProvider` — one is an interactive sign-in, the other a silent per-request
token.

### Widget

Put `UI/XsollaIdAccountWidget.prefab` on a canvas and call `widget.Bind(auth, avatars)`
(pass `showBackPackLink: false` to hide the Backpack button). Define the ten
localization keys listed in [`UI/README.md`](com.longleap.xsollaid/UI/README.md)
(`btn_xsolla_sign_in`, `btn_log_out`, `xsolla_err_*`, …). The prefab ships without a
font — use the TMP default or set **Font Override** on the instance.

### Startup account flow

```csharp
// 1. Before the save service: who plays.
var scope = new XsollaCloudSaveScope(baseUrl, gameId, prefs, savesPath: savesPath);
var gate  = new AccountGate(auth, scope, scope.TryPeekAccountAsync, () => MyStartupScreen.Create());
AccountGateResult who = await gate.RunAsync(ct);

// 2. Provider follows the answer.
ICloudSaveProvider provider = who.IsGuest
    ? new NullCloudSaveProvider()
    : new XsollaCloudSaveProvider(baseUrl, gameId, who.AccountId, savesPath: savesPath);
var saves = new CloudSaveService(provider, prefs);

// 3. Bring the local save in line with the owner, then initialize.
var rules = new SaveMergeRules(SaveMergeRule.PreferAccount)
    .Add("stat_*", SaveMergeRule.Max)
    .Add("ach_*_unlocked", SaveMergeRule.Or);
var outcome = await new SaveSwitch(saves, scope, rules).RunAtStartupAsync(who.AccountId, who.AccountEntries);
if (outcome.IsFailure() && !who.IsGuest)
{
    saves.Dispose();
    saves = new CloudSaveService(new NullCloudSaveProvider(), prefs);   // play local-only this launch
}
await saves.InitializeAsync();
```

The startup screen is yours: implement `IAccountScreen`. An account change in-game
restarts the game; call `AccountGate.ForgetGuestChoice()` before an in-game "Sign in".

### Leaderboards and leagues (`XSOLLA_ID` only)

```csharp
var leaderboards = new LeaderboardService("dev_", leaderboardProvider, authProvider, () => playerName);
var board = leaderboards.GetBoard(new BoardId("score"), SortDirection.Descending);
await board.SubmitScoreAsync(1500);   // queued
await leaderboards.FlushAsync();      // sent
var top10 = await board.GetTopAsync(10);

var league = new LeagueService("dev_", "weekly", leagueProvider, () => playerName);
league.RewardClaimed += r => wallet.Add(r.Reward);   // credited exactly once
await league.RefreshAsync();
await league.SubmitScoreAsync(runScore);
```

Providers: `XsollaLeaderboardProvider` / `XsollaLeagueProvider`, `FirebaseLeaderboardProvider`, or the null ones.

**Pitfalls.**
- Don't copy `XsollaId.jslib` or `xid-callback.html` into `Assets/`; don't create a `XsollaIdCallbacks` object — the package handles all three.
- `XSOLLA_ID_DEBUG` (F9/F10/F11 debug sign-in) must not ship to the portal.
- Working reference (in the Darkslide game, not this repo): `Assets/Scripts/Bootstrap/Loading/Tasks/XsollaIdTask.cs`.

**Details:** [`com.longleap.xsollaid/README.md`](com.longleap.xsollaid/README.md), [`UI/README.md`](com.longleap.xsollaid/UI/README.md), [`CONTEXT.md`](com.longleap.xsollaid/CONTEXT.md).

---

## com.longleap.bootstrap

**What it is.** Startup infrastructure: a sequential loading pipeline of small
tasks with weighted progress and per-step timing logs, a service container
(`ServiceContainer`) with a static access point (`GameServices`), optional
Addressables loading (`OptionalAddressable`) and `LoadSceneTask`. The steps
themselves are game code.

**Install.** `"com.longleap.bootstrap": "file:LongLeap/com.longleap.bootstrap"`. Pulls
Addressables. Assembly `LongLeap.Bootstrap`.

**How to use.**

```csharp
// Entry point (a MonoBehaviour created before the first scene)
var services = new ServiceContainer();
GameServices.Bind(services);

var pipeline = new LoadingPipeline()
    .Add(new GameConfigTask())
    .Add(new LoadSceneTask("Game"), weight: 5f)
    .Add(new MainMenuTask());

pipeline.ProgressChanged += p => loadingBar.Value = p.Value;
await pipeline.RunAsync(services, cancellationToken);
// On shutdown: services.Dispose(); GameServices.Unbind(services);
```

A step:

```csharp
public sealed class AudioServiceTask : LoadingTask
{
    public override async Task ExecuteAsync(LoadingContext context)
    {
        var library = await OptionalAddressable.LoadAsync<AudioLibrary>(AudioLibrary.AddressableKey, context);
        context.Services.Register<IAudioService>(new AudioService(library));
    }
}
```

A scene object that may wake up before its service exists:

```csharp
_wait = GameServices.WhenAvailable<IPlayerService>(Bind);   // dispose _wait in OnDestroy
```

**Pitfalls.**
- Register under the contract callers ask for (`Register<IAudioService>(...)`); without a type argument it registers under the concrete type.
- A failed `LoadingTask` is skipped; add `protected override bool IsCritical => true;` to stop loading instead.
- Frame-based (`Awaitable`), no threads — WebGL-safe.

**Details:** [`com.longleap.bootstrap/README.md`](com.longleap.bootstrap/README.md), [`CONTEXT.md`](com.longleap.bootstrap/CONTEXT.md).

---

## com.longleap.ui

**What it is.** The team's UI toolkit, logic only (prefabs, art and fonts stay in
the game):
- a **window system** — a catalog of Addressable window prefabs with preload/unload rules (`IWindowService`, `WindowService`, `WindowBase`);
- **ready-made windows** for LongLeap features — `SettingsWindow`, `AchievementsWindow`, `DailyRewardsScreen`, `DailyQuestPanel`, `UserBackPackWindow`;
- **notifications** — a priority-queued popup system (always compiled).

Each feature module is its own assembly and **compiles only when its feature
package is in the manifest**.

**Install.** `"com.longleap.ui": "file:LongLeap/com.longleap.ui"` (+ tools). Pulls uGUI,
Input System, Addressables. Add the feature packages whose windows you want.

**How to use.**

1. Make a UI root prefab with `WindowService`. Fill its `Windows` catalog with `WindowEntry` rows (Addressable window prefab, `Preload`, `Unload On Close`). Row order is draw order.
2. A window prefab: a `WindowBase` subclass on the root, visible content under a `Root` child, marked Addressable.
3. In code:

   ```csharp
   windows.OnCreated<SettingsWindow>(w => w.Bind(settingsService, save));   // data in, during loading
   windows.SetHome<MainMenuWindow>();
   await windows.OpenAsync<SettingsWindow>();
   await windows.CloseAsync<SettingsWindow>();
   ```

4. Implement the seams the windows need: `IUiSounds`, `IMusicInfo`, `IPlayerStatsSource` (quests), `IBackPackProductCatalog` (backpack).
5. Notifications: `NotificationBootstrapper.Show("Title", "Message")` from anywhere (calls are buffered until the view is ready), or use `NotificationService` + your own `NotificationSystemView` subclass.

**Pitfalls.**
- Never keep a reference to an unload-on-close window between showings; pass data through `OnCreated`.
- Creating windows goes through Addressables — the window prefabs must be Addressable.
- See *Migrating a game* in the package README for GUID notes when switching from copied scripts.

**Details:** [`com.longleap.ui/README.md`](com.longleap.ui/README.md), [`Notifications.md`](com.longleap.ui/Notifications.md), [`CONTEXT.md`](com.longleap.ui/CONTEXT.md).

---

## com.longleap.buildmanager

**What it is.** One editor window, **LongLeap ▸ Build Manager**, showing every
build-relevant option in the project. You stage changes without recompiling, apply
them in one batch (one define write, one domain reload), pick a preset (GamePix,
Xsolla ID, Local), and dispatch the build. Options, presets, warnings and build
targets are declared by other packages through `LongLeap.BuildManager.Options` and
discovered automatically. Editor only — nothing goes into the player.

**Install.** `"com.longleap.buildmanager": "file:LongLeap/com.longleap.buildmanager"`.
No dependencies. Reference `LongLeap.BuildManager.Options` from an editor assembly to
declare options.

**How to use.**

- Open the window, toggle options, press **Apply**, then **Build**. Build is disabled while changes are staged, a recompile is pending, or scripts don't compile.
- Output folder: **Project Settings ▸ LongLeap ▸ Build Manager** (default `Builds/WebGL`, stored in `ProjectSettings/LongLeapBuildManagerSettings.asset` — commit it).
- Declare your own option:

  ```csharp
  internal sealed class MyGameOptions : IProjectOptionSectionProvider
  {
      public const string CheatsId = "mygame.cheats";
      public IEnumerable<ProjectOptionSection> GetSections()
      {
          yield return new ProjectOptionSection("mygame", "My Game", 100, new ProjectOption[]
          {
              new DefineOption(CheatsId, "MYGAME_CHEATS", "Cheats (MYGAME_CHEATS)",
                               "Debug cheat menu. Must be off in release builds.", required: false, order: 10),
          });
      }
  }

  // Optional matching menu checkmark that can never disagree with the window:
  [MenuItem("My Game/Cheats")] static void Toggle() => ProjectOptionMenu.Toggle(MyGameOptions.CheatsId);
  [MenuItem("My Game/Cheats", true)] static bool Validate() => ProjectOptionMenu.Validate("My Game/Cheats", MyGameOptions.CheatsId);
  ```

  Other extension points: `IProjectOptionPresetProvider`, `IProjectWarningProvider`, `IProjectBuildTargetProvider`.

**Pitfalls.**
- Defines are written for **WebGL only**.
- An editor assembly that enables a define must never have that define in its own `defineConstraints`.
- No `[InitializeOnLoad]` may query `ProjectOptionRegistry`; descriptors hold paths and delegates, never `UnityEngine.Object`.

**Details:** [`com.longleap.buildmanager/README.md`](com.longleap.buildmanager/README.md), [`CONTEXT.md`](com.longleap.buildmanager/CONTEXT.md).

---

# Internal packages (not released)

These stay in the repo for our own games. They are not part of a release, carry no
compatibility promise and are exempt from the documentation conventions.

| Package | What it is | Depends on | Gate |
|---|---|---|---|
| `com.longleap.skins` | Engine-agnostic skin catalog model (`SkinId`, `ISkinCatalog`). | — | — |
| `com.longleap.skins.spine` | Spine skin folders → customization categories (JSON layout), Skin Layout Editor. | skins (+ host Spine runtime) | — |
| `com.longleap.customization` | Player skin inventory, equipped outfit, colours (cloud-saved), and the UGUI customization panel. | core, skins | — |
| `com.longleap.devtools` | In-build debug widgets; currently a cloud save viewer. | core, buildmanager | `LONGLEAP_DEBUG` |

---

## com.longleap.skins

**What it is.** An engine-agnostic skin catalog model: what skins and categories
exist in the game (content, not player state). Typed ids `SkinId`, `SkinCategoryId`;
`SkinAvailability` (`Locked`, `OwnedFromStart`, `Default`, `Disabled`);
`ISkinCatalog` and its in-memory `SkinCatalog`. No Spine dependency.

**Install.** `"com.longleap.skins": "file:LongLeap/com.longleap.skins"`. Assembly
`LongLeap.Skins` (no references).

**How to use.** Usually you don't build the catalog by hand — `com.longleap.skins.spine`
builds it from a layout JSON (`SkinLayout.BuildCatalog`). For tests and tools,
`SkinCatalog.Add` is public, so a catalog can be built without assets. Which skins a
player owns is the job of `com.longleap.customization` (`InventoryService`).

**Details:** [`com.longleap.skins/README.md`](com.longleap.skins/README.md).

---

## com.longleap.skins.spine

**What it is.** Maps the skin folders of a Spine skeleton onto customization
categories, and edits that mapping. Runtime: `SkinLayout` (JSON model →
`ISkinCatalog`), `SkinLayoutSourceSO` (skeleton + layout JSON + icons),
`SpineAppearanceConfig` (colour zones, slot hide rules). Editor: the **Skin Layout
Editor** window.

**Install.** `"com.longleap.skins.spine": "file:LongLeap/com.longleap.skins.spine"`
(+ skins). **The host project must provide the Spine runtime** with its standard
assembly names `spine-unity` and `spine-csharp`, or this package does not compile.

**How to use.**

1. Open **LongLeap ▸ Skin Layout Editor**; drag Spine skin folders into categories; set titles, availability, default colours, colour zones, icons and hide rules.
2. Create a `SkinLayoutSourceSO` pointing at the skeleton and the layout JSON (icons are stored here, since JSON can't hold sprite references).
3. Load the asset however your game prefers (Resources, Addressables, a field) and build the catalog for `com.longleap.customization`.

**Details:** [`com.longleap.skins.spine/README.md`](com.longleap.skins.spine/README.md).

---

## com.longleap.customization

**What it is.** Character customization: what the player owns
(`InventoryService`), what they wear (`EquipmentService` — one skin per group, or
many for multi-select groups), what colour it is (`ColorService`), all cloud-saved,
plus the UGUI `CustomizationPanel` that drives them. Skeleton-agnostic: the host
implements three small interfaces for its own character.

**Install.** `"com.longleap.customization": "file:LongLeap/com.longleap.customization"`
(+ core, skins). Assemblies `LongLeap.Customization` and `LongLeap.Customization.UI`.

**How to use.**

1. Build the catalog (`com.longleap.skins.spine`), then the services in this order: `InventoryService` → `EquipmentService` (needs inventory) → `ColorService`. Persist with the `CloudSave*Persistence` classes over `ICloudSaveService` (keys `inventory_skins`, `equipped_{category}`, `player_colors`).
2. On your character, implement:
   - `ISkinTarget.SetSkins(IReadOnlyList<string>)` — wear exactly this set (required);
   - `IColorTarget` — `SetZoneColor`, `SetSkinColor` (optional; no colour button without it);
   - `IPreviewPose.Focus(string group)` — turn the preview to the edited group (optional).
3. Create materials from the shaders `LongLeap/Customization/HSVSquare` and `LongLeap/Customization/HueStrip` and put them on the palette and hue slider images.
4. Bind the panel: `BindCharacter` / `Bind` with the character, `BindPlayer(inventory, equipment, colors)` with the services.

Each service has a `Null*` implementation for builds without customization.

**Details:** [`com.longleap.customization/README.md`](com.longleap.customization/README.md).

---

## com.longleap.devtools

**What it is.** In-build debug tooling. Currently one widget: `CloudSaveDebugPanel`,
an IMGUI overlay on the right side of the screen that lists the cloud save contents
(grouped by key prefix, filterable) and can delete keys. It shows the
`CloudSaveService`'s in-memory snapshot, so keys nothing touched this session are
absent. The runtime assembly **exists only with `LONGLEAP_DEBUG`**.

**Install.** `"com.longleap.devtools": "file:LongLeap/com.longleap.devtools"` (+ core,
buildmanager). Assembly `LongLeap.DevTools`.

**How to use.**

1. Turn on **LongLeap ▸ Cloud Save Panel (LONGLEAP_DEBUG)** (or the row in Build Manager).
2. Spawn the panel from game code, guarded by the define:

   ```csharp
   #if LONGLEAP_DEBUG
   LongLeap.DevTools.CloudSaveDebugPanel.Spawn(
       () => GameServices.TryGet<ICloudSaveService>(out var s) ? s : null,   // however your game reaches the service
       writeScope: "local + Firebase cloud_saves/{uid}");
   #endif
   ```

   `writeScope` is just a label telling the tester how far a delete travels (e.g. local only under `GAMEPIX`).

**Pitfalls.** Turn `LONGLEAP_DEBUG` off for release builds.

**Details:** [`com.longleap.devtools/CONTEXT.md`](com.longleap.devtools/CONTEXT.md) (no README).

---

## Scripting defines

| Define | Effect | Menu |
|---|---|---|
| `GAMEPIX` | Builds `LongLeap.GamePix`. Without it the assembly does not exist. | `GamePix / Required / Plugin Enabled (GAMEPIX)` |
| `GAMEPIX_JS` | GamePix Xsolla ID sign-in through `XsollaIdAuthJS` (own jslib) instead of `XsollaIdAuthGamePix` (plugin `Gpx.User`). For plugins without the user API. | `GamePix / Optional / Xsolla ID via JS Bridge (GAMEPIX_JS)` |
| `GAMEPIX_ALLOW_ANALYTICS` | Switch for game code: allow GA4 in the portal build. Off by default. | `GamePix / Required / Allow External Analytics (GA4)` |
| `GAMEPIX_AD_DEBUG` | Switch for game code: compile its on-screen ad log (`AdsDebug.Lines`). **Must not ship to the portal.** | `GamePix / Optional / Show Ad Debug Overlay` |
| `XSOLLA_ID` | Builds `LongLeap.XsollaId.Web` (plus the Firebase/Null leaderboard assemblies) and installs the WebGL callback page. Without it not a single request reaches `id.xsolla.com`. | `Xsolla ID / Required / Sign-in Enabled (XSOLLA_ID)` |
| `XSOLLA_ID_DEBUG` | On-screen debug sign-in: F9 in, F10 out, F11 state. WebGL only. Cascades off with `XSOLLA_ID`. **Must not ship to the portal.** | `Xsolla ID / Optional / Debug Panel (on-screen)` |
| `LONGLEAP_DEBUG` | Builds `LongLeap.DevTools`. | `LongLeap / Cloud Save Panel (LONGLEAP_DEBUG)` |

**Each is settable from two places** — the menu item above, or the matching row in
Build Manager. They cannot disagree: the menu items are thin wrappers over
`ProjectOptionMenu.Toggle` / `.Validate`. The difference is timing — a menu click
writes the define immediately (and triggers a domain reload), while Build Manager
stages it until Apply.

All are **WebGL-target** defines, matching this project's only platform.

---

## Conventions

**Assembly naming.** `com.longleap.stats` → `LongLeap.Stats` (Runtime),
`LongLeap.Stats.Editor`, `LongLeap.Stats.Tests`. Runtime code in `Runtime/`, editor
code in `Editor/`, EditMode tests in `Tests/EditMode/`.

**Two docs per package:**

- `README.md` — how to use it: public API tables, initialization, usage snippets, internals.
- `CONTEXT.md` — the vocabulary: what each term means, which synonyms to avoid, invariants and deliberate limits. Read this before naming anything.

Every released package has both, in English. The
[internal packages](#internal-packages-not-released) are exempt: `customization`,
`skins` and `skins.spine` have no `CONTEXT.md`, and `devtools` has no `README.md`.

**Typed newtypes over raw strings.** A raw `string` crossing a package boundary is
a smell. All are `IEquatable<T>` and validate their input, but they do not share
one shape:

| Newtype | Package | Invalid input |
|---|---|---|
| `UserId`, `IdToken` | core | `None` sentinel + `IsValid`; throws on `""` |
| `RewardKey` | core | `None` sentinel; throws on `""` |
| `CloudSaveKey` | core | **throws** on `. $ # [ ] /` |
| `BoardId` | xsollaid | `None` sentinel; regex-validated — invalid input logs an error and becomes `"invalid_board_id"` (`IsValid == false`) |
| `RewardId` | dailyrewards | `None` sentinel; throws on `""` |
| `StatPath` | stats | **throws**; `IsNone` for `default` |
| `EventName` | analytics | `None` sentinel + `IsValid`; logs an error, service drops the event |
| `SkinId`, `SkinCategoryId` | skins (internal) | `IsNone` for `default`; throws on `""` |
| `LocKey` | localization | null and `""` are the same empty key (`IsEmpty`); the only one that is not a `readonly struct` (Unity serialization) |

**Versions and dependencies.** Each package keeps its own version in
`package.json` (most are `0.1.0`). These are embedded packages, so no registry
checks that `package.json` matches what the `.asmdef` files reference — run the
check before a release instead:

```bash
python tools/check_dependencies.py          # all packages
python tools/check_dependencies.py com.longleap.stats
```

It fails when an assembly references a package that `package.json` does not
declare (directly or transitively), or when a declared LongLeap version is higher
than the package's own. The `.asmdef` is the truth; fix `package.json` to match.

**Optional dependencies.** An assembly that should compile only when another
package is present does not list that package in `package.json`. Instead its
`.asmdef` has a `versionDefines` entry for the package (`expression: "0.0.0"`,
define `LONGLEAP_<NAME>`) and the same define in `defineConstraints`. Without the
package the assembly is skipped rather than failing. Used by the `com.longleap.ui`
feature modules and by the `XSOLLA_ID` assemblies of `firebase` and `nullproviders`
(`LONGLEAP_XSOLLAID`). The check script understands this pattern.

---

## Working on the library

This is a **separate repository**. A change here needs two commits:

```bash
# in Packages/LongLeap
git add -A && git commit -m "..." && git push

# in the host project
git add Packages/LongLeap && git commit -m "Bump packages" && git push
```

Skipping the second commit leaves teammates on the old submodule pointer.

---

## Where to look next

| Question | File |
|---|---|
| What does this term mean? Can I rename it? | the package's `CONTEXT.md` |
| Full API table, internals | the package's `README.md` |
| What are the core contracts? | `com.longleap.core/README.md` |
| How do I add a build option? | `com.longleap.buildmanager/README.md` |
| How do I wire the Xsolla ID widget? | `com.longleap.xsollaid/README.md` and `UI/README.md` |
| How do notifications work? | `com.longleap.ui/Notifications.md` |
