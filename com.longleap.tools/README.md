# LongLeap Tools

Shared tools for every game. Currently: the **global device profile** (runtime), **page visibility** in the browser (runtime), **building blocks for code-built UI** (runtime) and the **WebGL page mode** (editor).

## Runtime UI

`RuntimeUi` (assembly `LongLeap.Tools.UI`, namespace `LongLeap.Tools.UI`) — text, background, slider, button, layout columns and rows. The settings panel (`com.longleap.settings`) and the `com.longleap.ui` windows build themselves with it: one class for everyone, so a change in the theme shows up in every window at once. It is a separate assembly so that `LongLeap.Tools` (the device profile) does not drag in UGUI and TextMeshPro.

### Theme

The palette, row sizes and default font live in a `RuntimeUiTheme` asset (ScriptableObject), one per game:

1. **Create ▸ LongLeap ▸ Runtime UI Theme**, name it `RuntimeUiTheme` and put it in any `Resources` folder — `RuntimeUi` finds it on its own.
2. Or set it from code before windows are built: `RuntimeUi.Theme = myTheme;` (reset on entering Play Mode).

Without an asset the `RuntimeUiTheme` defaults apply — the palette the package started with, so existing windows look the same as before. `RuntimeUi.Accent`, `RowHeight` and the rest are now properties that read the theme (they used to be `static readonly` / `const`).

The **Font** field is the text font used when the caller passes `null`. If there is no font in either place, the text is disabled (TMP without a font breaks the window layout) and a warning is logged once.

## Device profile

`DeviceInfo` (assembly `LongLeap.Tools`) is the single place that decides "what device is this". The rest of the code does not check `Application.isMobilePlatform`, the touchscreen or `UNITY_WEBGL` itself — it asks `DeviceInfo`.

| Profile (`DeviceProfile`) | What it is |
|---|---|
| `Desktop` | Native desktop build (and the editor): keyboard and mouse |
| `Mobile` | Native mobile build / tablet: touch |
| `WebDesktop` | Browser on a desktop (WebGL) |
| `WebMobile` | Browser on a phone / tablet (WebGL) |

```csharp
using LongLeap.Tools;

DeviceProfile profile = DeviceInfo.Profile;   // pick a config: switch / dictionary / asset field
if (DeviceInfo.IsMobile) { /* touch controls, native or in the browser */ }
if (DeviceInfo.IsWeb)    { /* browser */ }
if (DeviceInfo.IsDesktop) { /* keyboard and mouse */ }
```

Two axes: environment (`IsWeb`) and controls (`IsMobile`/`IsDesktop`). How "mobile" is decided:

1. A mobile platform, or a phone/tablet by `SystemInfo.deviceType` — mobile.
2. In the browser — the primary pointer: the media query `(hover: none) and (pointer: coarse)` via `LongLeapTools.jslib`. It is the same signal the orientation overlay in `index.html` uses, so the page and the game agree: a touchscreen laptop (primary pointer is a touchpad/mouse) is `WebDesktop`, an iPad with a desktop user agent is `WebMobile`. If the browser does not answer — the touchscreen (`Keyboard` always exists in the browser).
3. In a native build — a touchscreen without a keyboard.

The value is not cached (a touchscreen or keyboard may appear after startup). Read a long-lived profile once.

This is not the in-game control scheme ("whatever was pressed last", world prompts) — that is decided by the game's input code, which can take its starting scheme from `DeviceInfo.IsMobile`.

### Testing another profile

- Code / tests: `DeviceInfo.Override = DeviceProfile.WebMobile;` (reset on entering Play Mode).
- Editor: the **Tools ▸ Device Profile** menu (Auto / Desktop / Mobile / Web Desktop / Web Mobile) or `DeviceInfo.EditorOverride` (editor only). The choice is stored in EditorPrefs per project (the key includes `productGUID`) and applies from the next time windows are built — restart Play Mode. While it is not Auto, entering Play Mode logs a warning.

## WebGL page mode (editor)

The **Tools ▸ WebGL Page Mode** menu chooses how the game page behaves in the browser; a postprocessor applies it to `index.html` of the next WebGL build:

- **Fixed Aspect** — the aspect ratio comes from the canvas size in `index.html` (Player Settings), with margins around.
- **Free Aspect** — the canvas fills the whole browser window.

Game orientation — the **Tools ▸ WebGL Page Mode ▸ Orientation** submenu:

- **Auto** (default) — from WebGL Resolution in Player Settings: width < height is portrait, otherwise landscape.
- **Landscape** / **Portrait** — explicit, regardless of the resolution.

In both page modes a "rotate your device" overlay for the chosen orientation is added, plus a best-effort `screen.orientation.lock` in fullscreen. The overlay triggers only on touch devices (`hover: none` and `pointer: coarse`): a desktop browser window cannot be rotated, so there the game is just shown with margins. The overlay text follows the browser language (`navigator.languages`, the first language with a translation): en, ru, uk, de, fr, es, pt, it, pl, tr, ja, ko, zh; otherwise English. The translations are the `RotateTexts` table in `WebGlPageHtml`.

Pixel density on phones — the **Tools ▸ WebGL Page Mode ▸ Mobile Pixel Ratio 1** item (on by default): the postprocessor uncomments `config.devicePixelRatio = 1` in the default template. On phones the canvas then renders in CSS pixels — faster, but blurrier on high-density screens. Turn it off if image quality matters more than performance.

Mode, orientation and pixel density are project settings: they live in `ProjectSettings/LongLeapWebGlPage.asset` (`WebGlPageSettings`) and are committed, so the build is the same for every developer and on CI. The choice used to be stored in EditorPrefs; on the first menu open or first build without the file it is moved into the file (a console message says so) — commit the file.

The HTML edits themselves are pure functions in `WebGlPageHtml` (string in, string out, covered by tests); `WebGlPagePostprocessor` only reads the file, takes the settings and logs. Neither class depends on the game.

## Page visibility

`PageVisibility` (assembly `LongLeap.Tools`) tells whether the browser tab with the game is hidden: switched away from, minimized, or being closed. On the web this is the reliable "the player left" signal. Unity raises no callback when a tab closes, and window focus is also lost when the player clicks outside the canvas while the game stays on screen (a portal page around an iframe).

```csharp
using LongLeap.Tools;

PageVisibility.Changed += hidden => { if (hidden) Save(); };
if (PageVisibility.IsHidden) { /* tab is in background right now */ }
```

- Listens to `visibilitychange`, `pagehide` and `pageshow` through `LongLeapTools.jslib`. A back/forward cache restore counts as shown again. Duplicate states are filtered.
- The browser listener is registered on the first subscription and kept for the page's lifetime. `IsHidden` is current from then on.
- Only WebGL players report anything (`IsSupported`). Elsewhere, including the Editor, `IsHidden` stays false and `Changed` never fires, so keep `OnApplicationPause` for native mobile.
- Handlers run synchronously inside the browser event, on the main thread. An exception in one handler is logged and does not stop the others. Work started there is the last chance before the page may be gone; an awaited continuation may never run.

`com.longleap.analytics` has its own listener (`WebGlPageLifecycle`) so that it does not depend on this package.

## Assemblies

| Assembly | Contents |
|---|---|
| `LongLeap.Tools` | `DeviceProfile`, `DeviceInfo`, `PageVisibility`, `LongLeapTools.jslib` (depends on Unity.InputSystem) |
| `LongLeap.Tools.UI` | `RuntimeUi`, `RuntimeUiTheme` (depends on UGUI and TextMeshPro) |
| `LongLeap.Tools.Editor` | `WebGlPagePostprocessor`, `WebGlPageHtml`, `WebGlPageSettings`, the `Tools/Device Profile` menu |
| `LongLeap.Tools.Tests` | EditMode tests (`Tests/EditMode`); visible in the Test Runner when the package is listed in the manifest's `testables` |

Used by: `com.longleap.settings` (`LongLeap.Settings.UI` — `DeviceInfo` and `RuntimeUi`), `com.longleap.ui` (`LongLeap.UI.DailyRewards` — `RuntimeUi`).
