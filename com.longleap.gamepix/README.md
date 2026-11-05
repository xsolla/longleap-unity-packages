# LongLeap GamePix

Backend for the [GamePix](https://www.gamepix.com) web portal. It wraps the vendor
GamePix Unity plugin behind the LongLeap seams (`IAdsProvider`, `IPortalLocale`,
`IXsollaIdAuth`) and adds the editor tooling a portal build needs.

## Requirements

- Unity 6000.0+, WebGL.
- The vendor GamePix plugin at **`Assets/Plugins/GamePix`** (the path is hard-wired in
  the vendor code and cannot be moved).
- The **`GAMEPIX`** scripting define. The runtime assembly `LongLeap.GamePix` exists only
  with it; the editor assembly is always compiled, so the menu that turns `GAMEPIX` on
  is always there.
- Packages: `com.longleap.core`, `com.longleap.xsollaid`, `com.unity.nuget.newtonsoft-json`.

## Setup

1. Import the vendor plugin into `Assets/Plugins/GamePix`.
2. **GamePix / Required / Plugin Has defineConstraints** — restores the `GAMEPIX`
   constraint in the plugin's asmdefs (every plugin update wipes it; without it a plain
   WebGL build pulls the plugin in).
3. **GamePix / Required / Plugin Enabled (GAMEPIX)**.
4. If the plugin has no `Assets/Plugins/GamePix/User` folder, also turn on
   **GamePix / Optional / Xsolla ID via JS Bridge (GAMEPIX_JS)** (see below).
5. Build from **LongLeap / Build Manager** (target "GamePix: …") or from the plugin's own
   **GamePix / Safe builds** menu.

All toggles are also rows in the Build Manager window; the menu and the window read and
write the same options. The Build Manager warns when `GAMEPIX` is on but the plugin is
missing, the constraints are gone, or `GAMEPIX_JS` does not match the plugin.

## Runtime API (`LongLeap.GamePix`)

| Type | Implements | Notes |
|---|---|---|
| `GamePixAdsProvider` | `IAdsProvider` | Rewarded and interstitial ads, `ReportScore`. One request at a time, a 30 s response timeout (extended while the ad is on screen, capped at 600 s), one result per request. **The host must call `Tick()` every frame.** Writes to `AdsDebug`. |
| `GamePixPortalLocale` | `IPortalLocale` | The language the portal passed at launch, as an ISO 639-1 code. |
| `XsollaIdAuthGamePix` | `IXsollaIdAuth` | Xsolla ID through the plugin's `Gpx.User`. Compiled when `GAMEPIX_JS` is **off**. |
| `XsollaIdAuthJS` | `IXsollaIdAuth` | Xsolla ID through the package's own `GamePixXsollaId.jslib` → `window.GamePix.user`. Compiled when `GAMEPIX_JS` is **on**. |

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

// In a MonoBehaviour that lives for the whole game:
void Update() => (ads as LongLeap.GamePix.GamePixAdsProvider)?.Tick();
```

### Xsolla ID on the portal

GamePix signs the player in itself (its own window, refresh and token cache); the game
only asks for the ready answer. There is no PKCE, no callback page and no stored session;
the token is never persisted or logged. `CanSignOut` is `false` — the portal has no
sign-out. The player id is the JWT `sub` (the same key the standalone Xsolla ID sign-in
uses), falling back to `user.id`. A 404 from the SDK means "not inside the GamePix
Player" and hides sign-in; a 401 from `GetUser` means a guest.

**Which class:** `XsollaIdAuthGamePix` is preferred. `XsollaIdAuthJS` is for plugin
versions without the user API; it waits with `Task.Delay`, which can hang in a WebGL
player.

## Scripting defines

| Define | Effect |
|---|---|
| `GAMEPIX` | Builds `LongLeap.GamePix` and enables the vendor plugin assemblies and the `index.html` postprocess. |
| `GAMEPIX_JS` | Xsolla ID through `XsollaIdAuthJS` (jslib) instead of `XsollaIdAuthGamePix` (`Gpx.User`). Exactly one of the two is compiled. |
| `GAMEPIX_ALLOW_ANALYTICS` | Switch only — game code checks it before starting GA4 in a portal build. Off by default. |
| `GAMEPIX_AD_DEBUG` | Switch only — game code compiles its on-screen ad log (reading `AdsDebug.Lines`) under it. Turn off before review. |

## Editor tooling

- **index.html postprocess** (`GAMEPIX` WebGL builds): makes the canvas fluid so the game
  fits the portal iframe, strips "Unity Web Player |" from the title, and puts the GamePix
  SDK v3 tag first in `<head>` (mandatory per the GamePix guide). Runs before the vendor
  builder packs the `.gpx`.
- **GamePix / Package HTML5 Zip...** — packs a built WebGL folder for an HTML5 upload
  (instead of `.gpx`): forward-slash entry names, `index.html` at the root, `.gpx`
  pipeline files excluded, legacy SDK tag removed. Logs a summary and warns when the
  build is uncompressed (the GamePix menu build always disables compression — the `.gpx`
  compresses itself; for HTML5 enable Brotli + Decompression Fallback and build via the
  Build Manager).

## Tests

EditMode tests in `Tests/EditMode` (`LongLeap.GamePix.Tests`, requires `GAMEPIX`):
`GamePixAdsProviderTests` always; `XsollaIdAuthJSTests` only with `GAMEPIX_JS`.
