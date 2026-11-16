# LongLeap Backpack

Client for the BackPack service: player events grant inventory rewards, the game reads and spends those items. The player is identified by Xsolla ID.

**Event**:
A named game occurrence sent as `POST {eventsPath}` with `{gameId, xsollaId, event, eventId}`. For achievements, the event name is the achievement ID. `eventId` is the idempotency key: by default it is derived deterministically from `(gameId, xsollaId, eventName)` (`BackPackService.StableEventId`), so a retry never grants a reward twice.

**Reward / Backpack item**:
What the server grants for an accepted event. Read via `GetRewardsAsync` (`GET {rewardsPath}`) as `BackPackRewardItem`: `Sku` is the item type, `Id` is the concrete instance. All `attributes` / `custom_attributes` are parsed as they are into `Attributes` / `CustomAttributes` (`BackPackAttributes`). The package gives attributes no meaning: which ones exist (`reward`, `merge`, …) and what their values mean is decided by the client, e.g. `CustomAttributes.ValueIds("reward")`, `CustomAttributes.Find("merge")`.

**Spending**:
The game spends an item by deleting that one instance (`DeleteItemAsync(itemId)`). Success is decided by the per-item outcome in the response body (`item_deleted`), not by the HTTP status — the server answers 200 for every outcome.

**Environment**:
`BackPackConfigSO` holds three endpoint sets (Local, Development, Production); exactly one is active. Selected from the **BackPack** menu or the Build Manager. An empty base URL is a valid "off" switch.

**Identity**:
`BackPackIdentity` holds the signed-in player's Xsolla ID. `BackPackService` reads it on every request through a delegate, so sign-in/sign-out never require recreating the service. No ID means guest: nothing is sent.

**Service / Transport / Debug API**:
Game code depends on `IBackPackService`, implemented by `BackPackService` (configured) or `NullBackPackService` (switched off). All HTTP goes through `IBackPackTransport` via the internal `BackPackClient`, which never lets exceptions out except caller-requested cancellation. A request is either answered, fails in transport, or is not sent at all (`not_configured`, `no_xsolla_id`, `disposed`). Disposing the service aborts requests in flight. Destructive reset endpoints live in `BackPackDebugApi`, compiled only for the Editor and development builds.

The config holds only BackPack endpoints. Cloud save and leaderboard paths belong to whoever wires those providers (`com.longleap.xsollaid`).

## Relationships

- **Backpack → Achievements**: `BackPackAchievementStateStore` wraps `IAchievementStateStore`; an unlock is persisted only after the server accepts its event. Guests and an unconfigured service fall through to the inner store.
- **Backpack → Xsolla ID**: the sign-in flow (in Climber — `XsollaIdTask`) sets and clears `BackPackIdentity`.
- **Backpack → UI**: `com.longleap.ui` compiles `UserBackPackWindow` only when this package is installed (`LONGLEAP_BACKPACK` version define); it lists items, checks the connection and spends the selected item at run start.
- **Backpack → Core.Editor**: the environment is registered as a project option (`BackPackOptions`) with warnings for an empty `gameId` and for Local in a GamePix build.
