# Sign-in with Xsolla ID

OAuth2/OIDC authorization code + PKCE. The package is **self-contained**: nothing
needs to be copied into the project's `Assets/` — no scripts, no plugins, no
callback page.

## What's inside

| Where | What |
|---|---|
| `Runtime/` | contracts and orchestration, without a single network call |
| `Runtime.Web/` | HTTP, the browser window and `XsollaId.jslib`, plus [leaderboards and leagues](#leaderboards-and-leagues). Compiled only under the `XSOLLA_ID` define |
| `Runtime.UI/` | the account block widget |
| `Runtime.Accounts/` | the startup account flow: account or guest, and the guest-to-account save merge (see [below](#startup-account-flow)) |
| `UI/` | the widget prefab and sprites (see `UI/README.md`) |
| `Editor/` | the `Xsolla ID` menu and `xid-callback.html` |

**`XsollaId.jslib` sits NEXT TO `Runtime.Web/*.cs` on purpose.**
One half of the bridge is `[DllImport("__Internal")]` in `XsollaIdWebBrowser` and
`XsollaIdWebGLImageDecoder`, the other is seven functions in the jslib. When the
file lived in the game's `Assets/Plugins/WebGL/`, the package compiled but the
build failed at link time:

```
wasm-ld: error: undefined symbol: OpenXsollaIdPopup
wasm-ld: error: undefined symbol: XsollaIdBeginImageDecode
...
```

The error shows up at the very end of a WebGL build and gives no hint that a
file was not copied. Keeping both halves in one place is the only way to make
adding the package not depend on remembering this.

The receiver object name (`XsollaIdCallbacks`) is duplicated in the jslib and in
`XsollaIdCallbackReceiver.ReceiverName`. Change them only together.

## Setup

1. **Package.** Put it in `Packages/` (embedded) or reference it in `manifest.json`.
   Dependencies: `com.longleap.core`, `com.longleap.localization`,
   `com.unity.nuget.newtonsoft-json`, `com.unity.textmeshpro`.

2. **Define.** Menu `Xsolla ID / Required / Sign-in Enabled (XSOLLA_ID)`.
   Without it the `LongLeap.XsollaId.Web` assembly does not exist at all, and not
   a single request goes to `id.xsolla.com`. The define is set for WebGL.

3. **Config.** `Assets / Create / LongLeap Services / Xsolla ID Config`,
   place it at `Assets/Resources/Backend/XsollaIdConfig.asset`, fill in
   `clientId` and `redirectUri` for the active environment. The environment is
   switched from the same menu.

   An empty `clientId` is NOT a build error: sign-in reports `Unavailable` and
   the widget hides entirely. If the account block "did not appear", look here
   first; the factory logs exactly what is missing.

4. **Widget.** Put `UI/XsollaIdAccountWidget.prefab` on a canvas. Layout,
   palette and editor preview details are in `UI/README.md`.

5. **Services for the widget.** It does not look anything up by itself:

   ```csharp
   var auth = LongLeap.XsollaId.Web.XsollaIdFactory.Create(config, prefs);
   var avatars = LongLeap.XsollaId.Web.XsollaIdFactory.CreateAvatarLoader();
   widget.Bind(auth, avatars);
   ```

   Find the widget with `FindObjectsInactive.Include` if it lives on a disabled
   screen.

6. **Localization keys.** Ten of them, listed in `UI/README.md`. Without a
   localization service `Loc.Resolve` returns the key itself, and the button will
   read `btn_xsolla_sign_in`.

## What NOT to do

- Copy `XsollaId.jslib` into `Assets/Plugins/WebGL/` — it is already in the
  package, and a second copy gives two definitions of the same symbols.
- Place `xid-callback.html` next to the build by hand:
  `XsollaIdCallbackPagePostprocessor` puts it into the root of the WebGL build
  itself when the define is on, and removes it when it is off.
- Create a `XsollaIdCallbacks` object in the scene: it is created by
  `XsollaIdCallbackReceiver.Instance`.

## Font

The widget prefab ships without a font, so its labels use the project's TMP
default font (`TMP Settings` → Default Font Asset). To use another font, set
**Font Override** on the widget instance: it is applied to every label in the
widget, including those in states that are currently hidden. Per-label overrides
on the prefab instance keep working as well.

## Startup account flow

Assembly `LongLeap.XsollaId.Accounts`. The player picks **Xsolla ID** or
**guest** before the save service starts, and guest progress is merged into the
account on sign-in.

It decides **who plays this launch** and **whose save they play on**, once, at
startup. It draws no UI: the startup screen is the game's, behind
`IAccountScreen`. Vocabulary and invariants are in [CONTEXT.md](CONTEXT.md).

### What's inside

| Type | Role |
|---|---|
| `AccountGate` | Waits for the silent session restore; shows the startup screen only when there is no session; reads the account save. Returns `AccountGateResult`. |
| `AccountGateResult` | `AccountId` (`null` for a guest), `AccountEntries` (the account save; `null` if not read), `IsGuest`. |
| `IAccountScreen`, `AccountScreenBusy` | The startup screen, drawn by the game. |
| `SaveSwitch` | Run before `CloudSaveService.InitializeAsync`. Clears another owner's local save, uploads a local save the server lost, merges guest progress into the account. Returns `SaveSwitchOutcome`. |
| `SaveMergeRules`, `SaveMergeRule` | Per-key merge table: glob pattern → `Max`, `Min`, `Or`, `Union`, `PreferAccount`, `PreferLocal` or a custom `SaveMergeFunc`. The only game-specific part. |

### Setup

1. **Assembly.** Reference `LongLeap.XsollaId.Accounts` from the game's
   assembly definition. It compiles without the define.
2. **Define.** `XsollaCloudSaveScope` and `XsollaCloudSaveProvider` live in
   `LongLeap.XsollaId.Web`, which compiles only with the `XSOLLA_ID` define.
   Without it there is no sign-in in the build: skip the gate and play as a
   guest on the null provider.
3. **Startup screen.** Implement `IAccountScreen` (see the contract below).
4. **Merge rules.** Describe how each of your save keys merges.

### Using it in a game

```csharp
using LongLeap.Core.CloudSaves;
using LongLeap.Null;
using LongLeap.XsollaId;
using LongLeap.XsollaId.Accounts;
using LongLeap.XsollaId.Web; // XSOLLA_ID only

// 1. Before the save service: who plays.
var scope = new XsollaCloudSaveScope(baseUrl, gameId, prefs, savesPath: savesPath);
var gate = new AccountGate(auth, scope, scope.TryPeekAccountAsync, () => MyStartupScreen.Create());
AccountGateResult who = await gate.RunAsync(cancellationToken);

// 2. The provider follows the answer and never changes while the game runs.
ICloudSaveProvider provider = who.IsGuest
    ? new NullCloudSaveProvider()
    : new XsollaCloudSaveProvider(baseUrl, gameId, who.AccountId, savesPath: savesPath);
var saves = new CloudSaveService(provider, prefs);

// 3. Bring the local save in line with the owner, then initialize.
var rules = new SaveMergeRules(SaveMergeRule.PreferAccount)
    .Add("stat_*", SaveMergeRule.Max)
    .Add("ach_*_unlocked", SaveMergeRule.Or);
SaveSwitchOutcome outcome = await new SaveSwitch(saves, scope, rules)
    .RunAtStartupAsync(who.AccountId, who.AccountEntries);

// 4. The switch failed: the local save is not the account's yet, so don't sync
//    it with the account's cloud save. This launch plays on this device only.
if (outcome.IsFailure() && !who.IsGuest)
{
    saves.Dispose();
    saves = new CloudSaveService(new NullCloudSaveProvider(), prefs);
}

await saves.InitializeAsync();
```

Create the scope even when the gate is skipped (sign-in unavailable, no save
server): a guest launch must clear an account's local save rather than play on
it, and only the scope knows whose save it is.

**An account change restarts the game.** Sign-in or sign-out during the game
must go through a restart, so the gate and the switch run again. For a
"Sign in" button inside the game, call `AccountGate.ForgetGuestChoice()` first —
otherwise the gate remembers the guest choice for the app session and the
startup screen does not appear.

### AccountGate

The order:

1. Sign-in unavailable (`IXsollaIdAuth.IsAvailable == false`) → guest, no screen.
2. Wait for the silent session restore (`IsReady`), up to **12 s**. On timeout
   the startup screen is shown anyway; a late session is still picked up.
3. If availability turns off during the restore (some platforms, such as
   GamePix outside its player, report it only then) → guest, no screen.
4. **Session present** → read the account save (`tryPeek`, up to **15 s**).
   - Read, or not read but the local save already belongs to this account
     (`SaveSwitch.CanUseAccount`) → the account plays.
   - Otherwise → `ShowServerError()`: "Try again" repeats the read, "Play as
     guest" plays this launch as a guest (the session stays; the next launch
     tries again).
5. **No session**, and the guest was already chosen in this app session → guest.
6. **No session** → `ShowChoice(lastError)`: "Sign in" opens the Xsolla ID
   window and returns to step 4 on success; "Play as guest" → guest. A sign-in
   that throws, returns nothing, or succeeds without an account id comes back
   as `ShowChoice(InvalidState)`.

The screen is created lazily: a relaunch with a valid session and a readable
save never shows it. It is closed when `RunAsync` returns, throws or is
cancelled.

`tryPeek` must return `null` when the save could not be read and an empty
dictionary when the server has nothing — use
`XsollaCloudSaveScope.TryPeekAccountAsync`, not `PeekAccountAsync`, which
returns an empty dictionary for both.

### IAccountScreen contract

- **Invoke `SignInClicked` synchronously from the button's `onClick`** — no
  coroutine, no `await` in between. The gate calls `SignInAsync` inside the
  handler, and the browser blocks a popup opened outside a user gesture.
- The gate sets `SignInClicked`, `GuestClicked` and `RetryClicked` before every
  `Show*` call. A `null` handler means that button is not offered now: in
  `ShowChoice`, `RetryClicked` is `null`; in `ShowServerError`, `SignInClicked`
  is `null`.
- `ShowBusy` keeps the previous handlers set — the screen itself must make the
  buttons non-interactive.
- `ShowChoice(lastError)`: `lastError` is why the previous sign-in failed
  (for example `PopupBlocked`, `Network`); `null` means show the normal hint.
  `Cancelled` and `SessionExpired` are never passed — they are not errors.
- `Close()` is called once; destroy the screen there.
- The screen makes no decisions and picks all texts and their language itself.

### SaveSwitch

Run it **before** `CloudSaveService.InitializeAsync`: initialization merges the
local save with the cloud by timestamps, and after that there is nothing left to
compare.

| Local save belongs to | This launch | Result |
|---|---|---|
| guest | guest | `Unchanged` |
| an account | guest | local save cleared → `ClearedForGuest` (the account's cloud save is untouched) |
| this account | this account | `Unchanged`; if the server has nothing, the local save is uploaded → `UploadedLocal` |
| another account | this account | local save cleared → `ClearedForOtherAccount` |
| guest | an account | merged by the rules → `Merged` |

**The scope is switched last**, only once the local save really holds the new
owner's data: the reset finished, or every merged key was written. Any failure
before that — reading the local save, the reset, a write, a timeout, the
account save unknown — returns `Failed` and leaves the scope as it was, so the
next launch sees the same situation and does the work again. Repeating a merge
is safe: every built-in rule gives the same result a second time.

**On `Failed` with an account, play the launch on a local-only save**
(`outcome.IsFailure()`, step 4 of the example). The local save is not the
account's yet; initializing the account's cloud save on it would push it into
the account by timestamps, without the merge rules. A guest needs nothing — its
saves are local anyway.

`RunAtStartupAsync` never throws. `CanUseAccount` is the same check the gate
uses to decide whether an account can be played.

`new SaveSwitch(saves, scope, rules)` takes the `CloudSaveService` once. The
five-argument constructor takes the writer, the snapshot source and the local
reset separately, for fakes in tests.

Merging writes only keys that change: a key that already has the target value
on both sides, or that is missing locally and equals the account value
(initialization pulls it), is skipped. Written values get a fresh timestamp, so
they win when initialization reconciles the local save with the cloud.

Timeouts: **5 s** for reading or clearing the local save; writing the merge gets
0.1 s per key, between **10 s** and **60 s**. A timeout does not cancel the
writes — they still land in the local save — but the switch reports `Failed`,
because it can't tell when they finish.

### SaveMergeRules

```csharp
var rules = new SaveMergeRules(SaveMergeRule.PreferAccount)   // default for unmatched keys
    .Add("stat_best", SaveMergeRule.Max)                      // specific first
    .Add("stat_*", SaveMergeRule.PreferLocal)
    .Add("skins", SaveMergeRule.Union, listSeparator: ',')
    .Add("today_score", (key, input) =>                       // custom: sees both saves
        input.LocalValue("day") == input.AccountValue("day")
            ? SaveMergeRules.Apply(SaveMergeRule.Max, input.LocalValue(key), input.AccountValue(key))
            : input.AccountValue(key));
```

| Rule | Result |
|---|---|
| `PreferAccount` | The account's value; the guest's if the account has none. For state that can't be combined: equipped items, settings, reward calendars. |
| `PreferLocal` | The guest's value; the account's if the guest has none. |
| `Max` | The larger number. Unparsable counts as missing; if neither parses, the account's raw value is kept. Two integers are compared exactly, beyond `double` precision. |
| `Min` | The smaller **positive** number — zero and negatives mean "unknown" (for "unlocked at" timestamps). |
| `Or` | Set if set in either save (`"1"` or `"true"`, case-insensitive). |
| `Union` | Items of both lists, account items first, duplicates and empty items removed. Separator `'\n'` by default. |

- Patterns are globs where `*` matches any run of characters; no other
  wildcards. Matching is case-sensitive.
- **The first matching pattern wins** — put specific patterns before general
  ones.
- A custom rule returns the merged value, or `null` to delete the key.
- Numbers are parsed with the invariant culture.

### Notes

- All waits are frame-based (`Task.Yield` + `Stopwatch`), so every timeout
  works under WebGL, where `System.Threading` timers never fire.
- The guest choice is a static; it is reset on `SubsystemRegistration`, so it
  also works with domain reload disabled.
- Logs never include the account id.

## Leaderboards and leagues

Contracts, services and value types for leaderboards and leagues live in the `LongLeap.XsollaId.Web` assembly, so they exist **only with `XSOLLA_ID`**. Namespaces: `LongLeap.XsollaId.Leaderboards` and `LongLeap.XsollaId.Leagues`.

| Backend | Leaderboards | Leagues | Assembly |
|---|---|---|---|
| Xsolla server | `XsollaLeaderboardProvider` (+ `XsollaAccountAuthProvider` to use the account id as uid) | `XsollaLeagueProvider` | `LongLeap.XsollaId.Web` |
| Null | `NullLeaderboardProvider` | `NullLeagueProvider` | `LongLeap.Null.XsollaId` (`com.longleap.nullproviders`) |
| Firebase RTDB | `FirebaseLeaderboardProvider` | — | `LongLeap.Firebase.Leaderboards` (`com.longleap.firebase`) |

The null and Firebase assemblies are gated by `XSOLLA_ID` as well.

### Leaderboards

| Type | Kind | Purpose |
|---|---|---|
| `ILeaderboardService` | Interface | `GetBoard(BoardId, SortDirection)`, `FlushAsync`, `GetPlayerUidAsync`, `GetPlayerDisplayName`, event `OnTopRankAchieved(BoardId, rank)` |
| `LeaderboardService` | Sealed class | `new LeaderboardService(prefix, ILeaderboardProvider, IAuthProvider, Func<string> displayName = null)` |
| `ILeaderboard` | Interface | Per-board handle: `SubmitScoreAsync`, `GetTopAsync`, `GetMyRankAsync`, `GetSurroundingsAsync` |
| `ILeaderboardProvider` | Interface | Backend seam; receives the prefixed board id and the uid |
| `BoardId` | Readonly struct | `^[a-zA-Z][a-zA-Z0-9_-]{0,39}$`. An invalid id logs an error and becomes `"invalid_board_id"` (`IsValid == false`) |
| `BoardDefinition` | Readonly struct | `Id` + `Sort` |
| `SortDirection` | Enum | `Descending`, `Ascending` |
| `SubmitResult` | Readonly struct | `IsAccepted`, `IsNewBest`, `Score` |
| `PlayerRank` | Readonly struct | `Rank` (int?), `Score` (long?), `TotalPlayers` |
| `LeaderboardEntry` | Readonly struct | `Uid`, `DisplayName`, `Score`, `Rank` (1-based), `Timestamp` |
| `LeaderboardPage` | Sealed class | `Entries`, `Cursor`, `HasMore`, `Empty()` |

```csharp
var leaderboards = new LeaderboardService("dev_", leaderboardProvider, authProvider, () => playerName);
var board = leaderboards.GetBoard(new BoardId("score"), SortDirection.Descending);
await board.SubmitScoreAsync(1500);   // queued
await leaderboards.FlushAsync();      // sent
var top10 = await board.GetTopAsync(10);
```

#### How it works

- **Batching.** `SubmitScoreAsync` only queues the score, keeping the best pending one per board (higher for `Descending`, lower for `Ascending`). `FlushAsync` sends the queue; failed boards are re-queued. `SubmitResult.IsAccepted` means "queued", not "accepted by the server".
- **Prefix.** Every board id sent to the provider is `prefix + id` (e.g. `"dev_"` for development builds). Keep prefix + id within 40 characters.
- **Uid.** Taken from `IAuthProvider.LoginAsync`; if auth fails, a GUID stored in `PlayerPrefs` under `longleap_device_id` is used. UI must compare row uids with `GetPlayerUidAsync()`.
- **Display name.** From the resolver passed to the constructor; `"Player"` if none or empty.
- **Caching.** Top and surroundings pages are cached per board for the lifetime of the service and dropped after the player's own accepted submit. On a failed request the cached page (or an empty one) is returned.
- **Top-rank event.** After each accepted submit in `FlushAsync`, `OnTopRankAchieved` fires if the player's rank is 10 or better.

### Leagues

A weekly league for the current player: submit scores, read the state, receive rewards for closed periods.

| Type | Kind | Purpose |
|---|---|---|
| `ILeagueService` | Interface | `LeagueId`, `State`, `RefreshAsync`, `SubmitScoreAsync`, events `StateChanged`, `RewardClaimed`; debug: `DebugFillGroupAsync`, `DebugClosePeriodAsync` |
| `LeagueService` | Sealed class | `new LeagueService(prefix, leagueId, ILeagueProvider, Func<string> displayName = null)` |
| `ILeagueProvider` | Interface | Backend seam; every method throws on a failed request |
| `LeagueState` | Sealed class | `Tiers`, `Tier`, `Period`, `Group` (null until the player submits this period), `LastResult`, `Unclaimed` |
| `LeaguePeriod`, `LeagueGroup`, `LeagueEntry`, `LeagueResult`, `LeagueClaim` | Model | Period timing, group standings and zones, closed-period results, claim reply |

```csharp
var league = new LeagueService("dev_", "weekly", leagueProvider, () => playerName);
league.RewardClaimed += result => wallet.Add(result.Reward); // credit here, exactly once
await league.RefreshAsync();
await league.SubmitScoreAsync(runScore);
```

- A failed submit is kept and added to the next submit (within the same launch).
- Whenever a state arrives with unclaimed results, the service claims them. `RewardClaimed` fires only when the server confirms this call claimed the reward, so a reward is credited once across devices.

## Tests

EditMode tests, one assembly per runtime assembly:

| Assembly | Covers | Needs |
|---|---|---|
| `LongLeap.XsollaId.Tests` | sign-in flow, PKCE, origin guard, redirect derivation, config, session and session store, value types | — |
| `LongLeap.XsollaId.Web.Tests` | HTTP helpers, discovery, token client, browser callback receiver, avatar loader, cloud save provider and scope, leaderboard and league services, providers and `BoardId` | `XSOLLA_ID` |
| `LongLeap.XsollaId.UI.Tests` | account widget state logic | — |
| `LongLeap.XsollaId.Accounts.Tests` | startup gate, save switch, merge rules | — |

Unity runs package tests only for packages listed in the project's
`Packages/manifest.json` under `testables`:

```json
"testables": [ "com.longleap.xsollaid" ]
```

All fakes are hand-written (no mocking library); the shared ones live in
`Fakes.cs` of each test folder (`EditMode`, `EditMode.Web`, `EditMode.Accounts`).
`LongLeap.XsollaId.Web` exposes its internals (the JS callback receiver) to its
test assembly via `Runtime.Web/AssemblyInfo.cs`.
