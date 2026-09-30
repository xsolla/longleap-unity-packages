# LongLeap Backpack

Unity package for sending player events to the LongLeap BackPack service. The service can grant inventory rewards for accepted events. This package also adapts the LongLeap Achievements state store so achievement unlocks are sent to BackPack before they are persisted.

## Requirements and Dependencies

- Unity 6.0 (`6000.0`) or later.
- `com.longleap.achievements` `0.1.0`.
- `com.longleap.core` `0.1.0`.
- `com.longleap.stats` `0.1.0`.

The dependencies are declared in this package's `package.json`. In this repository, the LongLeap packages are embedded under `Packages/` and Unity resolves them there. In another project, add this package with Unity Package Manager using **Add package from disk** and its `package.json`, or install it from your organization's package source. Make the three declared LongLeap dependencies available to that project as well. `com.longleap.core` depends on `com.unity.nuget.newtonsoft-json` `3.0.2`, which Unity resolves through the package dependency graph.

## Install and Configure

1. In Unity, create a config asset with **Assets > Create > Blood Red Snow > BackPack Config**.
2. Save it at `Assets/Resources/Backend/BackPackConfig.asset`. The runtime loads it as `Resources/Backend/BackPackConfig`.
3. In the asset Inspector, set the game ID and endpoint values for each environment. The game ID must be registered with the BackPack service. The event path defaults to `/events`.
4. Select the environment using **BackPack > Required > Environment > Local**, **Development**, or **Production**. Local defaults to `http://127.0.0.1:5066`; Development and Production use the configured HTTPS service endpoints. Local is only reachable from the developer's machine.
5. To enable the project's integrated event/achievement sending path, select **BackPack > Required > Send Events to Server (BACKPACK)**. This adds or removes `BACKPACK` from the WebGL scripting define symbols. Switch the active build target to WebGL before testing the WebGL-specific integration in Play Mode.

The editor menu's config selection and environment commands expect the asset at the path above. With sending disabled, or with an empty base URL/game ID, the integrated achievement flow continues to use its normal state store without sending events. Do not ship a build configured for Local.

## Sending an Event

`BackPackService` needs a provider for the currently signed-in player's Xsolla ID, the configured base URL, game ID, and event path. The ID is read for each request, so login and logout changes take effect without recreating the service.

```csharp
using LongLeap.Backpack;
using UnityEngine;

var config = Resources.Load<BackPackConfigSO>(BackPackConfigSO.ResourcePath);
var backPack = new BackPackService(
	() => BackPackIdentity.XsollaId,
	config?.BaseUrl,
	config?.GameId,
	config?.EventsPath);

// After successful sign-in:
BackPackIdentity.SetXsollaId(xsollaId);

BackPackReply reply = await backPack.SendEventAsync("tutorial_completed");
if (!reply.Success)
	Debug.LogWarning($"BackPack rejected the event: {reply.Error}");
else if (reply.RewardGranted)
	Debug.Log($"Granted reward: {reply.RewardId}");

// On sign-out:
BackPackIdentity.Clear();
```

Use a server-recognized event name. A successful response (`Success`) means the event was accepted; it does not necessarily mean a reward was granted. `RewardGranted` and `RewardId` describe the reward result. Missing configuration, a missing Xsolla ID, HTTP rejection, malformed responses, and transport errors are returned as a failed `BackPackReply` with an `Error` value.

When omitted, `eventId` is deterministic for the tuple `(gameId, xsollaId, eventName)`, which makes retries of the same event idempotent. If the same event name can occur more than once for one player, pass a unique ID for each occurrence:

```csharp
BackPackReply reply = await backPack.SendEventAsync(
	"level_completed",
	eventId: levelRunId);
```

`BackPackIdentity` is only a small in-memory holder; this package does not perform sign-in. Set it after authentication and clear it on sign-out. Alternatively, pass your own `Func<string>` provider to `BackPackService` as in the example.

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
| `BackPackService.SendEventAsync(eventName, eventId = null)` | POST an event and return a `BackPackReply`. |
| `BackPackReply` | Check `Success`, `Error`, `RewardGranted`, and `RewardId`. |
| `BackPackService.IsAvailable`, `HasXsollaId`, `XsollaId`, `Url` | Inspect service configuration and current identity. |
| `BackPackService.StableEventId(gameId, xsollaId, eventName)` | Generate the same event ID used by the default event submission. |
| `BackPackIdentity.SetXsollaId(id)`, `Clear()` | Set or clear the built-in identity value. |
| `BackPackAchievementStateStore` | Gate achievement-state persistence on server acceptance. |
| `BackPackService.DeleteBackpackItemsAsync(sku = null, itemId = null)` | Delete a player's backpack items, optionally filtered by SKU or item ID. |
| `BackPackService.DeleteClaimsAsync(eventId = null, sku = null, backpack = true, force = false)` | Delete event/reward claims for the current player and game. |
| `BackPackHttpReply` | Inspect raw DELETE status, response body, or transport error. |

The DELETE operations are destructive and are intended for development/testing. The service's reset endpoints are available on the Development server; other environments may return `404`. Calling `DeleteBackpackItemsAsync()` without filters deletes the player's entire backpack. Deleting claims can allow an event to grant a reward again; review the returned HTTP status and body before treating a reset as complete.

## Editor Menu

- **BackPack > Select Config Asset** selects the expected config asset in the Project window.
- **BackPack > Required > Environment > ...** changes the active environment stored in that asset.
- **BackPack > Required > Send Events to Server (BACKPACK)** toggles the WebGL `BACKPACK` scripting define used by the project's conditional integration.

These editor commands configure the host project; they do not automatically register event names or provision a BackPack server/game ID.