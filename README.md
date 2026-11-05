# LongLeap Packages

<https://gitea.longleap.games/Long_Leap_Games/Packages>

Shared Unity package library for LongLeap games. 20 UPM packages in a single Git
repo, consumed as a submodule at `Packages/LongLeap` and referenced from the host
project with `file:` paths.

Every package is embedded (local), so an Editor change is immediately live —
there is no publish step.

## Unity version

**Unity 6000.0 – 6000.4.** The floor is what every `package.json` declares
(`"unity": "6000.0"`). The ceiling comes from the **GamePix plugin**, not from
the packages themselves — **Unity 6000.5 requires updating the GamePix plugin**
before the project will build. Treat an Editor upgrade past 6000.4 as a GamePix
task first.

---

## Consuming the library

The host project declares each package explicitly in `<project>/Packages/manifest.json`:

```json
"com.longleap.core": "file:LongLeap/com.longleap.core",
"com.longleap.stats": "file:LongLeap/com.longleap.stats",
```

A package present in the submodule but absent from that manifest is simply not
compiled. Add the line to pull it in.

Those `file:` paths are relative to the project's `Packages/` folder, so they
encode where the submodule lives: `file:LongLeap/...` resolves only if the
submodule sits at `Packages/LongLeap`. **The location is a convention, not a
requirement** — you can mount the submodule elsewhere, but then `.gitmodules`
and every `file:` entry in the manifest have to change together. Change one and
Unity fails to resolve the packages.

> `manifest.json` at the repo root is **not** read by consumers — Unity only
> reads the host project's `Packages/manifest.json`.

Clone with the submodule:

```bash
git clone --recurse-submodules <project-url>
# or, in an existing clone
git submodule update --init --recursive
```

---

## Architecture

Three words carry the whole design. They are defined in
`com.longleap.core/CONTEXT.md` and used consistently everywhere.

| Term | Meaning |
|---|---|
| **Service** | A feature module, defined by its interface (`ICloudSaveService`, `IStatSystem`). What game code talks to. |
| **Provider** | The interface a backend implements (`ICloudSaveProvider`, `IAuthProvider`). The swap seam. |
| **Backend** | A concrete provider implementation — Firebase, Null, GamePix, WebGL. Game code never sees one directly. |

Each service is constructed on its own (`new CloudSaveService(provider)`). The
container that holds them (`ServiceContainer`) and the loading pipeline come from
`com.longleap.bootstrap`; which services to build, and in what order, is game code.

> `core/CONTEXT.md` also defines a **`ServiceBuilder`** with a fluent `Build()`
> returning a `ServiceContainer`. **No `ServiceBuilder` exists anywhere in the repo** —
> that entry is stale; the container is `ServiceContainer` in `com.longleap.bootstrap`. Do not write
> code against `ServiceBuilder`.

Consequences worth knowing before you add anything:

- **Contracts live in `core`, implementations live in backend packages.** A
  feature package never references a backend package; game code picks the
  backend when it constructs the service.
- **Null backends are opt-in, never a silent fallback.** They ship in `Runtime`
  (not `Tests`) and must be chosen explicitly at construction.
- **Avoid the names** *Manager*, *Controller*, *System*, *Adapter*, *Stub*,
  *Mock* for these roles — each `CONTEXT.md` lists the rejected synonyms for its
  own vocabulary.

---

## Package map

**Foundation** — no dependency on other LongLeap packages

| Package | What it is |
|---|---|
| `com.longleap.core` | Core contracts and domain types (auth, cloud saves, ads/portal, storage, experiments), plus the `CloudSaveService` implementation and the Unity storage implementations (`UnityPlayerPrefsAdapter`, `PlayerPrefsPersistentStorage`, `FilePersistentStorage`). Everything depends on this. |
| `com.longleap.localization` | `Loc` static facade over Unity.Localization, `LocKey` newtype, `LocalizedText` component. |
| `com.longleap.tools` | `DeviceInfo` — the one place that decides the device profile (desktop / mobile / web desktop / web mobile); `RuntimeUi` — code-built UI bricks for the settings and UI windows, styled by a per-game `RuntimeUiTheme` asset; `WebGlPagePostprocessor` — WebGL `index.html` page mode (fixed / free aspect), rotate-device overlay in the browser's language, mobile pixel ratio, stored in `ProjectSettings/LongLeapWebGlPage.asset`; `PageVisibility` — browser tab hidden/shown event (the WebGL replacement for focus/pause as the "player left" signal). |

**Features**

| Package | What it is | Depends on |
|---|---|---|
| `com.longleap.stats` | Hierarchical stat tree (`kill:monk:grunt`), wildcard subscriptions, accumulate/max cascading, cloud-persisted. | core |
| `com.longleap.achievements` | Goals with objectives; progress derived from stat subscriptions, unlocks fire notifications. | core, stats |
| `com.longleap.dailyrewards` | Streaks, cycles (`Once`/`Loop`), grace period, UTC reset window, calendar view-model. | core |
| `com.longleap.dailyquests` | JSON-defined quest pool, weighted daily set per UTC reset window, progress derived from stats (delta since issue), rewards claimed by the player. | core, stats |
| `com.longleap.settings` | Typed settings (bool / int / float / enum) with auto-generated UI and per-group cloud persistence. | core, localization, tools |
| `com.longleap.analytics` | GA4 Measurement Protocol analytics: validated events, batching with retry/backoff and a bounded queue, sessions and engagement time, consent switch, user ID/properties, A/B experiment tagging. Pluggable backends and HTTP transports, including a WebGL `sendBeacon` transport (survives tab close); `AnalyticsFlushDriver` hooks it to the player loop, `WebGlPageLifecycle` flushes when the browser tab is hidden. | core |
| `com.longleap.remoteconfig` | Typed remote parameters with inline defaults: PlayerPrefs cache of parameters and A/B experiments, shared in-flight fetch, cancellation, throttling, deferred activation. Backend-agnostic; Firebase provider in `com.longleap.firebase`. | core |

**Backends** — provider implementations, chosen by game code at construction

| Package | What it is | Depends on | Gate |
|---|---|---|---|
| `com.longleap.firebase` | Production: anonymous auth, cloud saves, remote config, and leaderboards (`LongLeap.Firebase.Leaderboards`, only with `XSOLLA_ID`). REST only, no Firebase SDK. | core, remoteconfig | — |
| `com.longleap.nullproviders` | Null-object backends for the core seams: auth, cloud saves, ads, portal locale; plus leaderboards and leagues (`LongLeap.Null.XsollaId`, only with `XSOLLA_ID`). Guest play, offline builds, builds without ads, tests. Feature seams keep their null backend in their own package. | core | — |
| `com.longleap.gamepix` | GamePix portal: interstitial + rewarded ads, portal language, Xsolla ID sign-in through the portal SDK, GamePix build tooling and HTML5 packaging. | core, xsollaid | `GAMEPIX` + the GamePix plugin at `Assets/Plugins/GamePix` |

**Platform**

| Package | What it is | Depends on |
|---|---|---|

**Identity**

| Package | What it is | Gate |
|---|---|---|
| `com.longleap.xsollaid` | Xsolla ID sign-in (OAuth2/OIDC auth code + PKCE), a drop-in account widget, and the startup account flow (`LongLeap.XsollaId.Accounts`: account or guest before the save service starts, guest progress merged into the account). Self-contained — nothing to copy into `Assets/`. | `XSOLLA_ID` for the `.Web` assembly |

**Team kits** — what our games share on top of the services. Feature services are still
built by the game; these only carry the plumbing and the screens.

| Package | What it is | Depends on |
|---|---|---|
| `com.longleap.bootstrap` | Sequential loading pipeline of `ILoadingTask` steps, `ServiceContainer`, `GameServices` access point, optional Addressables loading, `LoadSceneTask`. | — (Addressables) |
| `com.longleap.ui` | Window system (catalog of Addressable window prefabs with preload/unload rules) and windows for settings, achievements, daily rewards, daily quests, backpack. Includes the priority-queued notification system (formerly `com.longleap.notifications`). Each feature module compiles only when its feature package is installed. Logic only — prefabs stay in the game. | tools (uGUI, Input System, Addressables); feature modules: the matching feature packages |

**Tooling**

| Package | What it is | Gate |
|---|---|---|
| `com.longleap.buildmanager` | One editor window (**LongLeap / Build Manager**) staging every build option, applying them in a single batch, dispatching the build. Discovers options through `LongLeap.BuildManager.Options` — never references the feature packages. | — |
| `com.longleap.devtools` | In-build debug widgets; currently a cloud save viewer. | `LONGLEAP_DEBUG` |

---

## Scripting defines

| Define | Effect | Menu |
|---|---|---|
| `GAMEPIX` | Builds `LongLeap.GamePix`. Without it the assembly does not exist. | `GamePix / Required / Plugin Enabled (GAMEPIX)` |
| `GAMEPIX_JS` | GamePix Xsolla ID sign-in through `XsollaIdAuthJS` (own jslib) instead of `XsollaIdAuthGamePix` (plugin `Gpx.User`). For plugins without the user API. | `GamePix / Optional / Xsolla ID via JS Bridge (GAMEPIX_JS)` |
| `GAMEPIX_ALLOW_ANALYTICS` | Switch for game code: allow GA4 in the portal build. Off by default. | `GamePix / Required / Allow External Analytics (GA4)` |
| `GAMEPIX_AD_DEBUG` | Switch for game code: compile its on-screen ad log (`AdsDebug.Lines`). **Must not ship to the portal.** | `GamePix / Optional / Show Ad Debug Overlay` |
| `XSOLLA_ID` | Builds `LongLeap.XsollaId.Web` and installs the WebGL callback page. Without it not a single request reaches `id.xsolla.com`. | `Xsolla ID / Required / Sign-in Enabled (XSOLLA_ID)` |
| `XSOLLA_ID_DEBUG` | On-screen debug sign-in: F9 in, F10 out, F11 state. WebGL only. Cascades off with `XSOLLA_ID`. **Must not ship to the portal.** | `Xsolla ID / Optional / Debug Panel (on-screen)` |
| `LONGLEAP_DEBUG` | Builds `LongLeap.DevTools`. | `LongLeap / Cloud Save Panel (LONGLEAP_DEBUG)` |

**Each is settable from two places** — the menu item above, or the matching row
in Build Manager. They cannot disagree: the menu items are thin wrappers over
`ProjectOptionMenu.Toggle` / `.Validate`, reading and writing the same option
the window does. The difference is timing — a menu click writes the define
immediately (and triggers a domain reload), while Build Manager stages it until
Apply.

`GamePix / Required` carries more than the define: plugin `defineConstraints`
and external GA4 analytics are options there too. The GamePix SDK v3 tag is not
an option: `GamePixIndexPostprocessor` always puts it first in `<head>`, as the
official GamePix guide requires.

All four are **WebGL-target** defines, matching this project's only platform.

The rule that keeps this workable: **an editor assembly that enables a define
must never carry that define in its own `defineConstraints`** — it would
disappear exactly when you need it. That is why `LongLeap.BuildManager.Options` keeps
`references: []` and `defineConstraints: []` permanently.

---

## Xsolla ID setup

Sign-in needs the define **and** a config asset. Either one missing and sign-in
reports `Unavailable`, the button stays disabled, and the widget hides itself —
it is not a compile error, so this is the first place to look when the account
block "did not appear".

### The asset

`XsollaIdConfigSO`, created from **Assets / Create / LongLeap Services / Xsolla
ID Config**, and it must live at exactly:

```
Assets/Resources/Backend/XsollaIdConfig.asset
```

That path is hardcoded (`XsollaIdConfigAccess.ConfigAssetPath`) and loaded via
`Resources.Load`, next to `FirebaseConfig`. Jump to it with **Xsolla ID /
Select Config Asset**, or the *Select Config Asset* action in Build Manager.

**Everything in the asset is a public identifier — commit it.** There is no
client secret and there cannot be one: a WebGL bundle is readable by anyone.
PKCE proves the request is genuine instead.

### Stage and Production are different clients

Not one client against two hosts — two separate registrations, each with its
own `clientId` and its own registered redirect URIs. Switch with
**Xsolla ID / Required / Environment / Stage | Production**.

| Field | Stage default | Production default |
|---|---|---|
| `issuerBaseUrl` | `https://id-staging.xsolla.com` | `https://id.xsolla.com` |
| `clientId` | *(empty — required)* | *(empty — required)* |
| `redirectUri` | `http://127.0.0.1:8081/xid-callback.html` | *(empty — required)* |

Shared across both: `scopes` and `allowedGameOrigins`.

**Required for the active environment:** `issuerBaseUrl`, `clientId`,
`redirectUri`. Build Manager raises a **build-blocking error** when `XSOLLA_ID`
is on and the asset is missing, or the active environment's `clientId` or
`redirectUri` is empty.

### Redirect URI

The server compares `redirect_uri` **character for character** — scheme, host,
port, path. A mismatch is refused server-side, and you only find out *after*
the sign-in window opens, in wording that does not say why.

For builds we host ourselves the address is **derived at runtime**, not read
from the asset. `XsollaIdRedirect.TryDerive` builds it from the page URL as
`origin + directory-of-page + xid-callback.html` — from the page's *directory*,
so a build served out of a subfolder (`https://host/game/index.html`) still
works. The callback page ships inside the build, placed at the WebGL root by
`XsollaIdCallbackPagePostprocessor`, and there are at least three addresses it
can end up at: `serve.py` on 127.0.0.1, the Pages domain, and object storage.
One field in the asset for all three would mean editing the asset before every
build — and forgetting it in a release.

The asset's `redirectUri` is the **fallback**, used when derivation is not
possible: the Editor, a standalone player, or a non-http scheme.

One trap: `xid-callback.html` is named in two places —
`XsollaIdRedirect.CallbackFileName` and the postprocessor's `FileName`. They
must stay in sync, or the sign-in window 404s.

### Allowed game origins

`allowedGameOrigins` is checked *before* the window opens, and it is
**deliberately separate from the redirect URI — the two need not match.** The
game's origin and the callback page's origin are allowed to differ, so the
check is membership of this list, not a comparison against `redirectUri`. An
empty list means no check: let the server decide, rather than silently blocking
sign-in over an unfilled config.

It catches the case that costs the most time — `serve.py` prints `127.0.0.1`
but you typed `localhost`. Those are different origins, and sign-in fails.

### Scopes

Default `openid offline email profile`.

**`offline`, not `offline_access`.** That is the scope that makes this Hydra
instance issue a refresh token; without it the player signs in again after
every page reload. Production discovery advertises both, but this client is
registered for `offline` — asking for `offline_access` can come back
`invalid_scope` and break sign-in altogether, not just the refresh.

### Init and sign-in

One `#if` for the whole game — everything downstream sees interfaces only:

```csharp
IXsollaIdAuth auth;
IXsollaIdAvatarLoader avatars;

#if XSOLLA_ID
// Resources-relative path: no "Assets/Resources/" prefix, no ".asset".
var config = Resources.Load<XsollaIdConfigSO>("Backend/XsollaIdConfig");

auth = XsollaIdFactory.Create(
    config, new UnityPlayerPrefsAdapter(), deriveRedirectFromPage: true);
avatars = XsollaIdFactory.CreateAvatarLoader();
#else
auth    = new UnavailableXsollaIdAuth();
avatars = new UnavailableXsollaIdAvatarLoader();
#endif

// Labels first: without a localization service `Loc` returns the key itself,
// and the button would read "btn_xsolla_sign_in".
if (!Loc.IsReady)
    Loc.Init(myLocalizationService);

// Bind before checking IsAvailable. With sign-in unavailable the widget hides
// itself; skip Bind and it sits in "checking" forever. Inactive-inclusive
// search matters when the widget lives in a disabled menu prefab.
FindAnyObjectByType<XsollaIdAccountWidget>(FindObjectsInactive.Include)
    ?.Bind(auth, avatars);

if (!auth.IsAvailable)
    return;

auth.SignedIn  += OnSignedIn;    // unsubscribe in the matching teardown
auth.SignedOut += OnSignedOut;

// Silent restore from the stored refresh token — never opens a window.
// Do not await at boot: nothing on the loading screen depends on an account.
_ = auth.RestoreAsync();
```

Then the button:

```csharp
async void OnSignInClicked()
{
    XsollaIdResult result = await _auth.SignInAsync();

    if (result is XsollaIdResult.Success success)
        Debug.Log($"signed in as {success.Session.ShortDisplayName}");
    else if (result is XsollaIdResult.Failure failure)
        Debug.LogWarning($"{failure.Code}: {failure.Message}");
}
```

What the API guarantees, so you do not code around it:

- **Nothing ever throws.** Every failure arrives as `XsollaIdResult.Failure`
  with an `XsollaIdErrorCode`. Pick the player-facing string from `Code`;
  `Message` is log text.
- **`Create` returns a stub, not an error,** when the config is incomplete. An
  unfilled `clientId` is a normal state on a branch without a registration, so
  `IsAvailable` is the single check UI needs — no `#if` in any screen.
- **`SignInAsync` is re-entrant-safe.** Called again while one is in flight, it
  returns the *same* task and opens no second window.
- **Read `Current` / `IsSignedIn` only once `IsReady`.** Before that the answer
  is "restore hasn't finished", not "guest".
- **`IXsollaIdAuth` is not `IAuthProvider`.** This one is interactive and fires
  once on a click; `IAuthProvider.LoginAsync` is the cheap, silent per-request
  Firebase token call. Both exist on purpose.

Working reference: `Assets/Scripts/Bootstrap/Loading/Tasks/XsollaIdTask.cs`.

### Known limitation

The widget prefab references a specific TMP font asset. In another project that
reference is missing and the labels silently render with no font — override
`m_fontAsset` on your prefab instance.

---

## Conventions

**Assembly naming.** `com.longleap.stats` → `LongLeap.Stats` (Runtime),
`LongLeap.Stats.Editor`, `LongLeap.Stats.Tests`. Runtime code in `Runtime/`,
editor code in `Editor/`, EditMode tests in `Tests/EditMode/`.

**Two docs per package** — the convention, not yet the reality:

- `README.md` — how to use it: public API tables, initialization, usage
  snippets, internals worth knowing.
- `CONTEXT.md` — the vocabulary: what each term means, which synonyms to avoid,
  invariants, and deliberate limits. Read this before naming anything.

Current gaps: `gamepix` has neither, `devtools` has no `README.md`. `xsollaid/README.md` is
also the only package doc written in Russian.

**Typed newtypes over raw strings.** A raw `string` crossing a package boundary
is a smell. All are `IEquatable<T>` and validate their input, but they do not
share one shape:

| Newtype | Package | Invalid input |
|---|---|---|
| `UserId`, `IdToken` | core | `None` sentinel + `IsValid` |
| `BoardId` | core | `None` sentinel, regex-validated |
| `RewardKey` | core | `None` sentinel; throws on `""` |
| `RewardId` | dailyrewards | `None` sentinel |
| `CloudSaveKey` | core | **throws** on `. $ # [ ] /` |
| `StatPath` | stats | **throws**; `IsNone` for `default` |
| `EventName` | analytics | `None` sentinel + `IsValid`; logs an error, service drops the event |
| `LocKey` | localization | null and `""` are the same empty key (`IsEmpty`); the only one that is not a `readonly struct` (Unity serialization) |

**Versions.** Each package keeps its own version in `package.json` (most are
`0.1.0`). Nothing is enforced by a registry (these are embedded packages), so a
`package.json` dependency list can drift from what the `.asmdef` actually
references without anything failing. Treat the
`.asmdef` as the truth and fix the manifest to match.

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
| How do I call this? | the package's `README.md` |
| What are the core contracts? | `com.longleap.core/README.md` |
| How do I add a build option? | `com.longleap.buildmanager/README.md` |
| How do I wire the Xsolla ID widget? | `com.longleap.xsollaid/README.md` and `UI/README.md` |
