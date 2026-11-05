# LongLeap UI

The team's UI toolkit: a window system and ready-made windows for the LongLeap features.
Logic only — prefabs, art and fonts stay in the game.

## Modules

Every feature module is a separate assembly that compiles **only when its feature package
is in the project manifest** (`versionDefines` + `defineConstraints`). The package itself
depends on uGUI, Input System, Addressables and `com.longleap.tools` (used by the daily rewards
cards). Notifications are the exception: they live in this package and always compile
(see [Notifications.md](Notifications.md)).

| Assembly | Needs | What it is |
|---|---|---|
| `LongLeap.UI` | — | Window system (`IWindowService`, `WindowService`, `WindowBase`, `WindowEntry`, `MenuPageWindow`), widgets (`MenuButtonPressFeedback`, `MenuButtonHoverAudio`, `MenuClickAudioBinder`, `UiFitScale`, `TextColorSwitcher`, `RemoteSpriteCache`, `MusicTrackToast`), seams `IUiSounds` / `IMusicInfo`. |
| `LongLeap.UI.Settings` | settings, localization | `SettingsWindow` — window shell over `SettingsPanel` from the settings package: rows come from `SettingsWindowConfig` picked by device profile; save on close. |
| `LongLeap.UI.Achievements` | achievements, localization | `AchievementsWindow`, `AchievementItemView`. |
| `LongLeap.UI.DailyRewards` | dailyrewards, localization (+ tools) | `DailyRewardsScreen`, `DailyRewardDayView`. |
| `LongLeap.UI.DailyQuests` | dailyquests, localization | `DailyQuestPanel`, `DailyQuestItemView`, `DailyQuestRewardFlyout`, `PlayerStatRowView`, seam `IPlayerStatsSource`. |
| `LongLeap.UI.BackPack` | backpack, xsollaid | `UserBackPackWindow`, `BackPackItemView`, `BackPackRewardView`, seams `IBackPackProduct` / `IBackPackProductCatalog`. |
| `LongLeap.Notifications` | — | Notification core in `Runtime/Notifications/Core`: priority queue (`INotificationService`, `NotificationService`, `NotificationData`), `NotificationSystemView` base, `ITimer` / `CoroutineTimer`. No LongLeap dependencies. |
| `LongLeap.UI.Notifications` | — | `NotificationBootstrapper` (static `Show`, queues until ready), `GameNotificationView`, `NotificationPopup` (Animator or a code fade). |

## Windows

The UI root is a prefab with `WindowService` on it. Windows are **not** inside it: the root
holds the `Windows` catalog — rows of `WindowEntry` (window prefab as an Addressable reference,
`Preload`, `Unload On Close`). The window type and the row label are read from the prefab in
the editor. **Row order is draw order**: a lower row draws over an upper one.

| Preload | Unload On Close | Behaviour |
|:-:|:-:|---|
| ✔ | | created at startup, lives until exit |
| | | created on first open, then lives until exit |
| | ✔ | created on every open, destroyed and released on close |
| ✔ | ✔ | created ahead for the first show, released after closing |

A window prefab: a `WindowBase` subclass on the root, everything visible under a `Root`
child (the window toggles it). The window must be Addressable.

```csharp
await windows.OpenAsync<SettingsWindow>();   // create if needed, then open
await windows.CloseAsync<SettingsWindow>();  // close; unload if Unload On Close
var hud = await windows.LoadAsync<MyHud>();  // create without showing
windows.OnCreated<SettingsWindow>(w => w.Bind(...)); // runs for every instance, before it shows
windows.SetHome<MainMenuWindow>();           // where MenuPageWindow's "back" leads
```

Data reaches windows through `OnCreated` during loading: a window may not exist yet or may be
re-created after an unload, so never keep a reference to an unloading window between showings.

## What the game provides

The package knows nothing about the game's services. The game implements the seams and binds
them during loading:

| Seam | Used by | Typical implementation |
|---|---|---|
| `IUiSounds` (`UiSound` → clip) | click binder, rewards, quest flyout | over the game's audio service |
| `IMusicInfo` | `MusicTrackToast` | over the game's audio service |
| `IWindowService.SetHome<T>()` | `MenuPageWindow` "back" | the game's main menu |
| `SettingsWindow.Bind(service, save)` | settings | the game's `ISettingsService` and its save |
| `DailyRewardsScreen.Bind(service, sounds, rewardIcon)` | reward cards | icons from the game config |
| `IPlayerStatsSource` | `DailyQuestPanel` | game stats + wallet |
| `IBackPackProductCatalog` + `BindRewards(catalog, isRunActive)` | backpack | what each reward key gives the player |

The main menu, the intro and the window prefabs are game code: every game has its own.

## Tests

| Assembly | Mode | Covers |
|---|---|---|
| `LongLeap.Notifications.Tests` (`Tests/EditMode`) | EditMode | `NotificationService` queue, `NotificationSystemView` lifecycle, `NotificationData` defaults |
| `LongLeap.Notifications.PlayModeTests` (`Tests/PlayMode`) | PlayMode | `CoroutineTimer` |
| `LongLeap.UI.Tests` (`Tests/Windows`) | PlayMode | `WindowService` without Addressables: unknown windows, catalog validation, home window, EventSystem ownership |

Add `com.longleap.ui` to `testables` in the project manifest to run them. Creating windows from
the catalog goes through Addressables and is not covered by tests.

## Migrating a game

The package scripts keep the GUIDs the scripts had when they were extracted from Darkslide.
A game whose prefabs still reference those GUIDs switches by deleting its copies from
`Assets/`; the references then resolve to the package scripts.

Darkslide itself has re-GUIDed copies (so both could coexist): migrating it means pointing its
prefab references back from the new GUIDs to the package GUIDs, then deleting the copies.

Either way, update the catalog rows' `_typeName` afterwards (open the UI root prefab in the
editor) — window types now live in `LongLeap.UI.*` assemblies.
