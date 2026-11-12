# LongLeap Backpack

Unity package for the LongLeap BackPack service. It sends player events that can grant inventory rewards, reads and spends the items in the player's backpack, and adapts the LongLeap Achievements state store so achievement unlocks are sent to BackPack before they are persisted.

## Requirements and Dependencies

- Unity 6.0 (`6000.0`) or later.
- `com.longleap.achievements` `0.1.0`.
- `com.longleap.buildmanager` `0.1.0` (editor only: the environment option and config warnings in the Build Manager).
- `com.longleap.core` `0.1.0`.

The dependencies are declared in this package's `package.json`. In this repository, the LongLeap packages are embedded under `Packages/` and Unity resolves them there. In another project, add this package with Unity Package Manager using **Add package from disk** and its `package.json`, or install it from your organization's package source. Make the declared LongLeap dependencies available to that project as well. `com.longleap.core` depends on `com.unity.nuget.newtonsoft-json` `3.0.2`, which Unity resolves through the package dependency graph.

## Install and Configure

1. In Unity, choose **BackPack > Select Config Asset**. If the config does not exist yet, it offers to create it at the expected path, creating missing folders.
2. The config must live at exactly `Assets/Resources/Backend/BackPackConfig.asset`, and there must be only one: the runtime loads it only as `Resources/Backend/BackPackConfig`. If you create it with **Assets > Create > LongLeap Services > BackPack Config** instead, that menu puts it in the selected folder — move it to this path. The editor searches the whole project for BackPack configs and reports a misplaced config or extra copies (see [Config Checks](#config-checks)).
3. In the asset Inspector, set the game ID and endpoint values for each environment. The game ID must be registered with the BackPack service. Empty paths fall back to the defaults (`/events`, `/rewards`, `/backpack/items`).
4. Select the environment using **BackPack > Required > Environment > Local**, **Development**, or **Production** (also available in the Build Manager). Local defaults to `http://127.0.0.1:5066`; Development and Production use the configured HTTPS service endpoints. Local is only reachable from the developer's machine.

The editor menu's config selection and environment commands expect the asset at the path above. An empty base URL is a valid "off" switch: no requests are sent and the achievement integration falls back to its normal state store. Do not ship a build configured for Local.

## Creating the Service

`BackPackService` implements `IBackPackService`. It needs a provider for the currently signed-in player's Xsolla ID and the active endpoint from the config. The ID is read for each request, so login and logout changes take effect without recreating the service. Depend on `IBackPackService` in game code.

```csharp
using LongLeap.Backpack;
using UnityEngine;

var config = Resources.Load<BackPackConfigSO>(BackPackConfigSO.ResourcePath);
IBackPackService backPack = new BackPackService(
	() => BackPackIdentity.XsollaId,
	config != null ? config.Active : null);

// After successful sign-in:
BackPackIdentity.SetXsollaId(xsollaId);

// On sign-out:
BackPackIdentity.Clear();
```

`BackPackIdentity` is only a small in-memory holder; this package does not perform sign-in. Alternatively, pass your own `Func<string>` provider to `BackPackService`.

None of the request methods throw on network or server failures: every failure comes back as a result with an `Error` value. Every method accepts an optional `CancellationToken`; cancelling it aborts the request and throws `OperationCanceledException` — the only exception the service lets through. Do not cancel `DeleteItemAsync` once sent: the server may already have deleted the item.

A request that was never sent because the service is unconfigured, the player is a guest, or the service is disposed fails with `not_configured`, `no_xsolla_id` or `disposed`; only real network failures are prefixed with `transport:`.

`BackPackService` is `IDisposable`: disposing it aborts requests in flight (they fail rather than throw) and stops new ones. Register it in a container that disposes services on quit. `IsAvailable` stays `true` after disposal, so the achievements wrapper does not mistake shutdown for "BackPack off" and persist an unlock without its event.

When BackPack is not configured, use `NullBackPackService.Instance` instead of `null`: it answers `not_configured` to everything and never reports a signed-in player.

All HTTP goes through `IBackPackTransport` (default: `UnityWebRequestBackPackTransport`). Pass your own implementation as the third constructor argument to test response handling without a server.

## Sending an Event

```csharp
BackPackReply reply = await backPack.SendEventAsync("tutorial_completed");
if (!reply.Success)
	Debug.LogWarning($"BackPack rejected the event: {reply.Error}");
else if (reply.RewardGranted)
	Debug.Log($"Granted reward: {reply.RewardId}");
```

Use a server-recognized event name. `Success` means the event was accepted; it does not necessarily mean a reward was granted. `RewardGranted` and `RewardId` describe the reward result.

When omitted, `eventId` is deterministic for the tuple `(gameId, xsollaId, eventName)`, which makes retries of the same event idempotent. If the same event name can occur more than once for one player, pass a unique ID for each occurrence:

```csharp
BackPackReply reply = await backPack.SendEventAsync(
	"level_completed",
	eventId: levelRunId);
```

## Reading and Spending Items

```csharp
BackPackConnectionResult connection = await backPack.CheckConnectionAsync();
if (!connection.IsConnected)
	return; // No response, 404 (wrong path or unknown game ID) or 5xx.

BackPackRewardsReply rewards = await backPack.GetRewardsAsync();
foreach (BackPackRewardItem item in rewards.Items) // Never null; empty on failure.
	Debug.Log($"{item.Sku} ({item.Id}): {item.Name}");

BackPackDeleteResult result = await backPack.DeleteItemAsync(item.Id);
if (result.IsDeleted)
	GrantInGame(item.CustomAttributes.ValueIds("reward")); // Attribute ids and meaning are up to the game.
```

`CheckConnectionAsync` uses a shorter timeout (5 seconds) because the player waits on it. `DeleteItemAsync` removes exactly one item instance; `IsDeleted` is `true` only when the server reports that this instance was deleted now, so it is safe to grant the in-game reward based on it.

## Achievements Integration

Wrap the achievement system's existing `IAchievementStateStore` with `BackPackAchievementStateStore`:

```csharp
using LongLeap.Achievements;
using LongLeap.Backpack;

IAchievementStateStore stateStore = new BackPackAchievementStateStore(
	existingStateStore,
	backPack);

var achievements = new AchievementSystem();
achievements.Initialize(
	provider: achievementProvider,
	stateStore: stateStore,
	gameBridge: achievementBridge,
	stats: statsSystem);
```

For a signed-in player with an available service, an achievement's ID is sent as its event name. The wrapped store persists the unlock only after BackPack accepts the event. If the request fails, it logs a warning and leaves the unlock unpersisted so it can be retried on a later session. If BackPack is unavailable or there is no Xsolla ID, the wrapper delegates directly to the existing store. `LoadAsync` and `DeleteAllAsync` are also delegated to that store.

## Public API

| API | Purpose |
| --- | --- |
| `IBackPackService` / `BackPackService(xsollaIdProvider, endpoint, transport = null)` | Game-facing service; all methods take an optional `CancellationToken`. `Dispose` aborts requests in flight. |
| `NullBackPackService.Instance` | BackPack switched off; use instead of `null`. |
| `SendEventAsync(eventName, eventId = null)` | POST an event and return a `BackPackReply`. |
| `GetRewardsAsync()` | Read the player's backpack as a `BackPackRewardsReply`. |
| `DeleteItemAsync(itemId)` | Spend one item instance; returns a `BackPackDeleteResult`. |
| `CheckConnectionAsync()` | Check that the server responds; returns a `BackPackConnectionResult`. |
| `IsAvailable`, `HasXsollaId`, `XsollaId` | Inspect service configuration and current identity. |
| `BackPackService.StableEventId(gameId, xsollaId, eventName)` | Generate the same event ID used by the default event submission. |
| `IBackPackTransport`, `BackPackHttpCall`, `BackPackHttpReply` | The single HTTP seam; `UnityWebRequestBackPackTransport` is the default. A reply is a status with body, a `TransportError`, or `NotSentReason`. |
| `BackPackDebugApi` | Destructive development-only endpoints (see below). |
| `BackPackIdentity.SetXsollaId(id)`, `Clear()` | Set or clear the built-in identity value. |
| `BackPackAchievementStateStore` | Gate achievement-state persistence on server acceptance. |
| `BackPackConfigSO` | Per-environment endpoint settings; `Active` is the selected set. |

## Debug API

`BackPackDebugApi` is compiled only in the Editor and in development builds (`UNITY_EDITOR || DEVELOPMENT_BUILD`), so code that uses it must be wrapped in the same condition. It is constructed like the service and returns raw `BackPackHttpReply` values for logging:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
var debug = new BackPackDebugApi(() => BackPackIdentity.XsollaId, config.Active);
BackPackHttpReply reply = await debug.DeleteClaimsAsync(eventId: "tutorial_completed");
Debug.Log(reply); // "http_200: …", "transport: …" or "not sent: no_xsolla_id"
#endif
```

`DeleteBackpackItemsAsync(sku = null, itemId = null)` and `DeleteClaimsAsync(eventId = null, sku = null, backpack = true, force = false)` are destructive. The claims reset endpoint is available on the Development server only; other environments return `404`. Calling `DeleteBackpackItemsAsync()` without filters deletes the player's entire backpack, and deleting by SKU also clears the matching reward claims. Deleting claims can allow an event to grant a reward again; review the returned HTTP status and body before treating a reset as complete.

## Tests

EditMode tests live in `Tests/EditMode` (`LongLeap.Backpack.Tests`) and run without a server through a fake `IBackPackTransport`. They cover response parsing, request URLs and bodies, `StableEventId`, cancellation and `Dispose`, `NullBackPackService`, `BackPackDebugApi` and `BackPackAchievementStateStore`. To see them in the Test Runner, the host project lists the package under `testables` in `Packages/manifest.json`.

## Editor Menu

- **BackPack > Select Config Asset** selects the expected config asset in the Project window. If it is missing, it offers to move a misplaced config there or, when there is none, to create one. If there are several configs, it selects all of them. The Build Manager warns about a missing config with a **Create Config Asset** fix.
- **BackPack > Required > Environment > ...** changes the active environment stored in that asset. The same option is shown in the Build Manager, with warnings for an environment that has a base URL but no game ID, and for Local combined with a GamePix build.

### Config Checks

The editor searches the whole project for `BackPackConfigSO` assets and reports any of these as an error:

- **Misplaced** — the only config is not at `Assets/Resources/Backend/BackPackConfig.asset`, so the game does not load it and BackPack is off. **Select Config Asset** offers to move it there, keeping its values.
- **Duplicates** — more than one config exists. The game uses only the one at the expected path; the others look configured in the inspector but are ignored. **Select Config Asset** selects all of them so the extras can be deleted.

Both are reported in the Console when a config asset is imported, created, moved, or copied, and once per editor session. The Build Manager shows them as errors that block the build. Deleting or moving away the config at the expected path logs a warning that BackPack is off.

These editor commands configure the host project; they do not register event names or provision a BackPack server/game ID.
