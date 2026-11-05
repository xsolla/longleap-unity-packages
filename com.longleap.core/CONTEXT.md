# LongLeap Core

Foundation interfaces and types shared across all LongLeap packages, plus the backend-agnostic services (cloud saves, leaderboards, leagues), the Unity implementations of the storage contracts, and the Editor framework for project options.

## Cross-cutting concepts

**Service**:
A feature module exposed to game code (auth, cloud saves, leaderboards, leagues, achievements, daily rewards). Defined by its interface. Game code holds services, typically through the `ServiceContainer` of `com.longleap.bootstrap`.
_Avoid_: Manager, controller, system

**Backend**:
A swappable implementation of one or more provider interfaces. Null backends of the core seams live in `com.longleap.nullproviders`; production backends live in `com.longleap.firebase`, `com.longleap.xsollaid` and `com.longleap.gamepix`. Game code never sees backends directly — it sees the service wrapping them.
_Avoid_: Adapter, provider implementation

**Provider**:
The interface contract a backend implements (e.g. `ICloudSaveProvider`, `IAuthProvider`, `ILeaderboardProvider`, `ILeagueProvider`). The seam at which the service can swap implementations.
_Avoid_: Driver, connector

**Null backend**:
A backend that does nothing and returns a safe default (success where that is harmless, otherwise "unavailable" / "not found"). Core-seam null backends ship in `com.longleap.nullproviders`; feature seams keep theirs next to the interface. Explicitly opted into at construction time — never a silent fallback.
_Avoid_: Stub, mock, no-op backend

**Prefix**:
A string prepended to board and league ids (e.g. `"dev_"`) so development builds play on a separate copy of the data.

## Auth

**AuthResult**:
Either `Success` (with an `IdToken` and `UserId`) or `Failure` (with an error string). Consumers pattern-match to get the token and uid — no null checks needed.

**IdToken**:
Typed wrapper around a JWT string. Guards at compile time against mixing it up with other strings, and never prints the token in `ToString()`.
_Avoid_: Token, JWT, credential

**UserId**:
Typed wrapper around a backend uid. Has `IsValid` and a `None` sentinel; value equality.
_Avoid_: UID, player ID

## Cloud Saves

**CloudSave**:
A key-value pair stored in the cloud. The value is always a string at rest.

**Envelope** (`CloudSaveEnvelope`):
The cloud format of one entry: `{"v": value, "ts": unix seconds}`. A value that is not an envelope is read as-is with timestamp 0 ("time unknown"), so saves from before the envelope still load.

**Local mirror**:
The copy of the save in `PlayerPrefs` (`cloudsave_{key}`, `cloudsave_ts_{key}`). The service is local-first: writes land in the mirror immediately and reach the cloud later.

**Dirty key** (`cloudsave_dirty`):
A key whose local value has not been confirmed by the cloud yet. Persisted, so it is pushed after a restart too.

**Scope** (`IAccountCloudSaveScope`):
Which owner's save the provider reads and writes. **Guest** is the anonymous uid; **account** is an external account id. State of the *provider* — the service only knows keys. Switching is one caller's job (the game's save switch, on sign-in and sign-out), and it persists so it survives a restart. `PeekAccountAsync` reads an account's save **without** switching scope or applying anything — that is what a "which save do you want?" window is built from.
_Avoid_: Profile, slot

**Key index** (`cloudsave_index`):
Persisted list of every key that has a local copy. Exists because neither `IPlayerPrefs` nor `PlayerPrefs` can enumerate keys, so without it `ResetLocalCacheAsync` could not clear the mirror completely — and a forgotten key with a newer timestamp would beat the cloud on the next load, leaking one identity's progress into another.

**Local mirror reset** (`CloudSaveService.ResetLocalCacheAsync`):
Forgets the local copy of the save and touches **nothing** in the cloud. Not to be confused with `DeleteAllAsync`, which erases the owner's save. Used on an identity switch.

**Batch push** (`ICloudSaveBatchProvider`):
When the provider supports it, dirty keys are collected for a short pause and sent in a few large requests instead of one request per key.

## Leaderboards

**Board**:
A named leaderboard with a unique `BoardId` and sort direction. The game defines boards (e.g. "Score", "Survival Time"); the service handles submission and retrieval.

**Entry**:
A single row on a leaderboard: rank, display name, score, and user identifier.

**Rank**:
Player's 1-based position on a board. The service can query "my rank" and "entries around my rank" without fetching the entire board.

**Flush**:
Sending queued scores. `SubmitScoreAsync` only queues (keeping the best score per board); `FlushAsync` sends.

## Leagues

**League**:
A recurring competition (e.g. weekly) with ordered **tiers**. Identified by a league id (prefix included).

**Period**:
One round of the league, with a start and end time. Numbered.

**Group**:
The set of players a player competes against in a period. A player joins a group by submitting their first score of the period. Places at the top are the **promotion zone**, places at the bottom the **demotion zone**.

**Result**:
How a closed period ended for the player: place, score, outcome (promoted / stayed / demoted) and reward.

**Claim**:
Taking a result's reward. The server confirms exactly one claim per result, so the reward is credited once even across devices.

## Ads and portal

**Portal**:
A web game platform hosting the build (e.g. GamePix). Provides ads, a score showcase and the player's language.

**Rewarded / interstitial ad**:
A rewarded ad grants something only on `AdResult.Completed`; an interstitial is shown between levels and grants nothing.

**Portal locale** (`IPortalLocale`):
The language the portal chose for the player, applied on first launch.

## Shared

**IPlayerPrefs**:
Abstraction over Unity PlayerPrefs. Implemented by `UnityPlayerPrefsAdapter` and, for tests, `InMemoryPlayerPrefs`.

**IPersistentStorage**:
Async key-value string storage. Implemented by `PlayerPrefsPersistentStorage` (all platforms, including WebGL) and `FilePersistentStorage` (a file per key; not for WebGL).

**RewardKey**:
Names what to grant. Packages never interpret it; the game maps keys to currencies, items or boosters.

**ResetSchedule**:
Daily reset math for a UTC reset hour, shared by daily rewards and daily quests.

**IExperimentInfoProvider**:
Exposes `ActiveExperiments` (`IReadOnlyList<AbExperimentInfo>`) for analytics auto-tagging. Implemented by `RemoteConfigService`.

**AbExperimentInfo**:
`ExperimentId` and `VariantIndex` of an active A/B experiment.

## Editor

**Project option**:
One build-affecting setting described as data: a define symbol, an enum field in an asset, or custom read/write code. Has a stable `Id`, a label, a tooltip, and is either **required** (needed for a working build) or **optional** (debug; keep out of release builds).

**Section**:
A package's group of options and **actions** (buttons with no stored value).

**Preset**:
A named set of option values (e.g. "GamePix build"). An **exhaustive** preset turns every option it does not name to its off value.

**Saved vs staged state**:
Saved state is what the project holds now (menus use it). Staged state is saved state plus unapplied edits (the Build Manager window uses it). Availability and warnings are computed against whichever state is passed in.

**Build target**:
One way to build the game. The applicable target with the highest priority builds.

**Project warning**:
A configuration problem reported by a package; may block the build and may offer a fix.
