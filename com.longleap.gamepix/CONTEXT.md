# LongLeap GamePix

Backend for the GamePix web portal: the vendor plugin behind LongLeap seams, plus the editor tooling a portal build needs. See README.md for setup.

**Vendor plugin**:
GamePix's own Unity plugin, installed by the game at `Assets/Plugins/GamePix`. The path is hard-wired in the vendor code (`Builder.cs`, `Emscripten.cs`) and cannot be moved; this package only wraps it. Avoid: *SDK* for the plugin — the **SDK** is the portal's JavaScript (`window.GamePix`), loaded by the page.

**Plugin constraints** (`GamePixAsmdefConstraints`):
The `GAMEPIX` entry in `defineConstraints` of both plugin asmdefs (`GamePix.asmdef`, `Editor/GamePix.Editor.asmdef`). Every plugin update overwrites the asmdefs without it, and a plain WebGL build then pulls in the plugin's externs and auto-bootstrap. Restored automatically after a plugin import (`GamePixPluginImportWatcher`) and on demand from **GamePix ▸ Required ▸ Plugin Has defineConstraints** / Build Manager. Both files must have it — a half-applied state breaks compilation.

**User API**:
`Gpx.User` (`Assets/Plugins/GamePix/User`), present only in newer plugin versions. Decides which Xsolla ID implementation compiles.

**GamePixAdsProvider** (`IAdsProvider`):
Rewarded and interstitial ads and `ReportScore`. One request at a time, a 30 s response timeout (extended while the ad is on screen, up to 600 s), exactly one result per request. The host calls `Tick()` every frame — the portal never reports a show that did not happen.

**GamePixPortalLocale** (`IPortalLocale`):
The language the portal passed at launch, as an ISO 639-1 code.

**Portal sign-in** (`XsollaIdAuthGamePix` / `XsollaIdAuthJS`, both `IXsollaIdAuth`):
Xsolla ID through the portal: GamePix signs the player in itself; the game asks for the ready answer. No PKCE, no callback page, no stored session, no sign-out (`CanSignOut == false`). Player id is the JWT `sub`. `XsollaIdAuthGamePix` uses the plugin's user API; `XsollaIdAuthJS` uses the package's own `GamePixXsollaId.jslib` for plugins without it (`GAMEPIX_JS`). Exactly one compiles.

**Index postprocess** (`GamePixIndexPostprocessor`):
Edits `index.html` of a `GAMEPIX` WebGL build: fluid canvas, clean title, GamePix SDK v3 tag first in `<head>` (mandatory). Runs before the vendor builder packs the `.gpx`.

**HTML5 zip** (`GamePixHtml5Packager`):
Packs a built WebGL folder for an HTML5 upload instead of `.gpx`.

## Defines

- `GAMEPIX` — builds `LongLeap.GamePix` and enables the plugin. The editor assembly always compiles, so the menu that turns it on always exists.
- `GAMEPIX_JS` — `XsollaIdAuthJS` instead of `XsollaIdAuthGamePix`.
- `GAMEPIX_ALLOW_ANALYTICS`, `GAMEPIX_AD_DEBUG` — switches read only by game code. `GAMEPIX_AD_DEBUG` must be off for review.

## Relationships

- **GamePix → Core**: implements `IAdsProvider`, `IPortalLocale`; writes to `AdsDebug`.
- **GamePix → Xsolla ID**: implements `IXsollaIdAuth`, so the account widget and the startup account flow work on the portal unchanged.
- **GamePix → Build Manager**: options, warnings (plugin missing, constraints gone, `GAMEPIX_JS` vs plugin mismatch) and the "GamePix: …" build target.
