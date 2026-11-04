# LongLeap Xsolla ID

Xsolla ID sign-in, the Xsolla-backed RewardService providers, the account
widget, and the startup account flow. How to use them is in
[README.md](README.md); this file is the vocabulary.

## Sign-in

**Sign-in** (`IXsollaIdAuth.SignInAsync`):
Interactive: opens the Xsolla ID window, once, on a button press. Calling it
again while one is running returns the same task.
_Avoid_: Login (that is `IAuthProvider.LoginAsync` in `core` — the silent,
per-request Firebase token call; both exist on purpose)

**Silent restore** (`RestoreAsync`):
Getting the session back at startup from the stored refresh token. Never opens
a window.

**Available** (`IsAvailable`):
Sign-in is possible at all: platform, define and config line up. The single
check UI uses to hide sign-in.

**Ready** (`IsReady`):
The silent restore has finished, successfully or not. Before that the state is
"unknown yet", not "guest".

**Session** (`XsollaIdSession`):
An immutable snapshot of who is signed in and the tokens that prove it. A token
refresh creates a new session object (`SessionUpdated`), not a new sign-in.

**Subject** (`XsollaIdSession.Subject`, `XsollaUserId`):
The `sub` claim — the only stable key of an Xsolla ID user. Not a Firebase uid.
_Avoid_: UID, user ID

## Startup account flow (`LongLeap.XsollaId.Accounts`)

Who plays this launch, and whose save they play on. Decided once, at startup,
before the save service starts.

**Owner**:
Whoever the save of this launch belongs to: an **account** or the **guest**.
Chosen once per launch by `AccountGate` and never changed while the game runs.
_Avoid_: User, profile, player (the player is a person; the owner is a save)

**Account**:
A signed-in Xsolla ID identity. Identified by its **subject**, passed around as
`AccountId`. Its save lives on the server.
_Avoid_: User ID, UID (those are Firebase terms in `core`)

**Guest**:
A player without an account. `AccountId == null` (or empty). Its save lives on
this device only.
_Avoid_: Anonymous, offline user

**Local mirror**:
The local copy of the save that `CloudSaveService` works on (PlayerPrefs). It
always belongs to exactly one owner, recorded by the **scope**.
_Avoid_: Cache (except in `ResetLocalCacheAsync`, which is the core name), local save file

**Scope** (`IAccountCloudSaveScope`, defined in `core`):
Records whose data sits in the local mirror. Persists across launches, so the
next launch can tell whether the mirror is the guest's, this account's, or
another account's.

**Account save**:
The account's save as read from the server, without applying it — a **peek**.
`null` means it could not be read; an empty dictionary means the server has
nothing for this account. The two are different on purpose.

**Startup screen** (`IAccountScreen`):
"Sign in with Xsolla ID" or "Play as guest", plus a server-error state with
"Try again". Drawn by the game; makes no decisions.
_Avoid_: Login window, auth popup (the popup is the Xsolla ID window the screen opens)

**Gate** (`AccountGate`):
The flow behind the startup screen. Its answer is `AccountGateResult`.

**Save switch** (`SaveSwitch`):
The step that brings the local mirror in line with the owner: clear another
owner's data, upload a mirror the server lost, or merge guest progress into
the account. Its result is `SaveSwitchOutcome`.
_Avoid_: Migration, sync (sync is what `CloudSaveService.InitializeAsync` does after)

**Merge rule** (`SaveMergeRule`, `SaveMergeRules`):
How one save key is combined when guest progress meets an account save. The
table of rules is the only game-specific part of the package.
_Avoid_: Conflict resolution strategy

### Invariants

- **One owner per launch.** An account change (sign-in or sign-out during the
  game) restarts the game, so the gate and the switch run again. Nothing is
  switched on the fly.
- **The switch runs before `CloudSaveService.InitializeAsync`.** Initialization
  merges the mirror with the cloud by timestamps; after it there is nothing
  left to compare.
- **No merge without the account save.** If a guest mirror meets an account
  whose save could not be read, the account is not used
  (`SaveSwitch.CanUseAccount`): the gate offers "Try again" or "Play as guest".
  The account's own mirror is the exception — it works local-first.
- **Only a guest mirror is merged.** A mirror that belongs to an account is
  cleared when a guest or another account plays — that progress still lives
  in the account's save on the server.
- **The scope is switched last.** Only after the local mirror really holds the
  new owner's data. A failure leaves the scope as it was
  (`SaveSwitchOutcome.Failed`), and the next launch does the same work again;
  the merge is safe to repeat. The switch never throws.
- **A failed switch never reaches the account's cloud save.** On `Failed` with
  an account, the launch plays on a local-only save: the mirror isn't the
  account's yet.
- **The guest choice lasts for the app session** (a static that survives a
  scene reload). A new launch asks again.
- **All waits are frame-based** (`Task.Yield` + `Stopwatch`).
  `System.Threading` timers never fire under WebGL.

### Deliberate limits

- Sign-in is Xsolla ID only (`IXsollaIdAuth`).
- No "which save do you want to keep?" dialog: saves are merged key by key.
- Glob patterns support `*` only.
