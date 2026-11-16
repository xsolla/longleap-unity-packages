# LongLeap Tools

Small shared tools every game uses: the device profile, browser page visibility, code-built UI bricks and the WebGL page mode. See README.md for usage.

**Device profile** (`DeviceProfile`, `DeviceInfo`):
What device the game runs on: `Desktop`, `Mobile`, `WebDesktop`, `WebMobile`. Two axes — environment (`IsWeb`) and controls (`IsMobile` / `IsDesktop`). `DeviceInfo` is the single place that decides it; game code does not check `Application.isMobilePlatform`, touchscreens or `UNITY_WEBGL` itself. In the browser "mobile" means the primary pointer is coarse (`(hover: none) and (pointer: coarse)`), the same signal the page's rotate overlay uses. Not cached: a keyboard or touchscreen can appear after startup. Not the in-game **control scheme** ("whatever was pressed last") — that belongs to the game's input code. Avoid: *Platform*, *DeviceType*.

**Profile override** (`DeviceInfo.Override`, `DeviceInfo.EditorOverride`):
Forces a profile for tests (code) or for the Editor (**Tools ▸ Device Profile**, stored per project in EditorPrefs). `Override` resets on entering Play Mode.

**Page visibility** (`PageVisibility`):
Whether the browser tab with the game is hidden — switched away, minimized or closing. On WebGL this, not focus, is the "player left" signal: focus is also lost when the player clicks outside the canvas on a portal page. Only WebGL players report (`IsSupported`); elsewhere `IsHidden` stays false and `Changed` never fires. Handlers run synchronously inside the browser event — the last chance before the page may be gone.

**Runtime UI** (`RuntimeUi`, assembly `LongLeap.Tools.UI`):
Code-built UI bricks — text, background, slider, button, columns and rows — shared by the settings panel and the `com.longleap.ui` windows so one change restyles every code-built window. A separate assembly so the device profile does not drag in uGUI and TextMeshPro.

**Runtime UI theme** (`RuntimeUiTheme`):
One ScriptableObject per game: palette, row sizes, default font. Found as `RuntimeUiTheme` in any `Resources` folder, or set with `RuntimeUi.Theme`. Without an asset the defaults apply; without any font, code-built text is disabled rather than breaking the layout.

**WebGL page mode** (`WebGlPageSettings`, `WebGlPageHtml`, `WebGlPagePostprocessor`):
How `index.html` behaves: **Fixed Aspect** or **Free Aspect**, orientation (Auto / Landscape / Portrait) with a localized rotate-device overlay on touch devices, and mobile pixel ratio 1. A project setting in `ProjectSettings/LongLeapWebGlPage.asset`, committed. `WebGlPageHtml` holds the pure string-to-string edits; the postprocessor only reads, applies and logs.

## Invariants

- `LongLeap.Tools` depends only on the Input System; anything needing uGUI/TMP goes to `LongLeap.Tools.UI`.
- The JavaScript side lives in the package (`LongLeapTools.jslib`) next to its C# callers.
- Nothing here knows about a particular game.

## Relationships

- **Settings → Tools**: `LongLeap.Settings.UI` picks its window config by `DeviceInfo.Profile` and builds rows with `RuntimeUi`.
- **UI → Tools**: `com.longleap.ui` windows (daily rewards cards) build with `RuntimeUi`.
- **Analytics ↛ Tools**: analytics has its own visibility listener (`WebGlPageLifecycle`) so it does not depend on this package.
