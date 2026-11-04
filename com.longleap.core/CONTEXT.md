# LongLeap Core

Foundation interfaces and types shared across all LongLeap packages, plus the backend-agnostic services (cloud saves, leaderboards, leagues) and the Unity implementations of the storage contracts.

## Cross-cutting concepts

**Service**:
A feature module exposed by a package (auth, leaderboards, achievements, cloud saves, daily rewards, notifications). Defined by its interface, consumed by game code via `ServiceContainer`.
_Avoid_: Manager, controller, system

**Backend**:
A swappable implementation of one or more service interfaces. Null backends of the core seams live in `com.longleap.nullproviders`; production Firebase backends live in `com.longleap.firebase`. Game code never sees backends directly — it sees the service wrapping them.
_Avoid_: Adapter, provider implementation

**Provider**:
The interface contract a backend implements (e.g. `ICloudSaveProvider`, `IAuthProvider`, `ILeaderboardProvider`). The seam at which the service can swap implementations.
_Avoid_: Driver, connector

**ServiceBuilder**:
Fluent API that wires services and backends together. `Build()` returns a `ServiceContainer`. Auth is required; leaderboards, achievements, cloud saves, and daily rewards are optional.

**ServiceContainer**:
Holds constructed service instances. Lifecycle-managed (`IDisposable`). The single entry point game code uses to access services.

**Null backend**:
A backend that does nothing and returns a safe default (success where that is harmless, otherwise "unavailable" / "not found"). Core-seam null backends ship in `com.longleap.nullproviders` Runtime; feature seams keep theirs next to the interface. Explicitly opted into at construction time — never a silent fallback.
_Avoid_: Stub, mock, no-op backend

## Auth

**AuthResult**:
A discriminated union: either `Success` (with an `IdToken` and `UserId`) or `Failure` (with an error string). Consumers must pattern-match to access the token and UID — no null-guards needed.

**IdToken**:
Typed wrapper around a Firebase JWT string. Compile-time guard against leaking raw tokens or refresh tokens.
_Avoid_: Token, JWT, credential

**UserId**:
Typed wrapper around a Firebase UID string. Has `IsValid` and a `None` sentinel. Value equality (`readonly struct : IEquatable<UserId>`).
_Avoid_: UID, player ID

## Cloud Saves

**CloudSave**:
A key-value pair stored in the cloud. Value is always a string at rest; typed access (`int`, `float`) is provided via extension methods that serialize transparently.

**Scope** (`IAccountCloudSaveScope`):
Which owner's save the provider reads and writes. **Guest** is the anonymous Firebase uid; **account** is an external account id. State of the *provider* — the service only knows keys. Switching is one caller's job (the game's save switch, on sign-in and sign-out), and it persists so it survives a restart. `PeekAccountAsync` reads an account's save **without** switching scope or applying anything — that is what a "which save do you want?" window is built from.
_Avoid_: Profile, slot

**Key index** (`cloudsave_index`):
Persisted list of every key that has a local copy. Exists because neither `IPlayerPrefs` nor `PlayerPrefs` can enumerate keys, so without it `ResetLocalCacheAsync` could not clear the mirror completely — and a forgotten key with a newer timestamp would beat the cloud on the next load, leaking one identity's progress into another.

**Local mirror reset** (`CloudSaveService.ResetLocalCacheAsync`):
Forgets the local copy of the save and touches **nothing** in the cloud. Not to be confused with `DeleteAllAsync`, which erases the owner's save. Used on an identity switch.

## Leaderboards

**Board**:
A named leaderboard with a unique `BoardId` and sort direction. The game defines boards (e.g. "Score", "Survival Time"); the service handles submission and retrieval.

**Entry**:
A single row on a leaderboard: rank, display name, score, and user identifier. Returned in pages with surrounding entries.

**Rank**:
Player's 1-based position on a board. The service can query "my rank" and "entries around my rank" without fetching the entire board.

## Shared

**IPlayerPrefs**:
Abstraction over Unity PlayerPrefs: `GetString/Int/Float`, `SetString/Int/Float`, `HasKey`, `DeleteKey`, `Save`. Implemented by `UnityPlayerPrefsAdapter` and, for tests, `InMemoryPlayerPrefs`.

**IPersistentStorage**:
Async key-value string storage. Implemented by `PlayerPrefsPersistentStorage` (all platforms, including WebGL) and `FilePersistentStorage` (a file per key; not for WebGL).

**IExperimentInfoProvider**:
Exposes `ActiveExperiments` (`IReadOnlyList<AbExperimentInfo>`) for analytics auto-tagging. Implemented by `RemoteConfigService`.

**AbExperimentInfo**:
Value-type: `ExperimentId` (string) and `VariantIndex` (int). Represents an active A/B experiment variant.
