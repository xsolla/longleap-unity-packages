# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Changes made after 0.1.0 while the version stayed at 0.1.0. Some are **breaking**
(see below) — release them as 0.2.0.

### Changed (breaking)

- `RuntimeUi` colors and sizes (`Accent`, `Text`, `TextDim`, `Backdrop`,
  `PanelBackground`, `RowBackground`, `SliderTrack`, `Listening`, `RowHeight`,
  `RowSpacing`, `LabelFontSize`, `ValueFontSize`) are now properties that read the
  theme instead of `static readonly` fields / `const`. Code that used them in
  `const` expressions, attribute arguments or `switch` cases no longer compiles.
- `DeviceInfo.EditorOverrideKey` removed; the EditorPrefs key is now per project
  (`EditorOverrideKeyPrefix` + `PlayerSettings.productGUID`). A profile chosen in
  **Tools ▸ Device Profile** before this change is not carried over — pick it again.
- WebGL page mode and orientation moved from EditorPrefs to
  `ProjectSettings/LongLeapWebGlPage.asset` (`WebGlPageSettings`). The old EditorPrefs
  choice is moved into the file on the first menu open or build — commit the file.

### Added

- `RuntimeUiTheme` (ScriptableObject, **Create ▸ LongLeap ▸ Runtime UI Theme**):
  palette, row sizes and default font for `RuntimeUi`. Loaded from
  `Resources/RuntimeUiTheme` or set via `RuntimeUi.Theme`; without an asset the
  original palette applies. Text without any font is hidden with a one-time warning.
- `DeviceInfo.EditorOverride` (editor only): the **Tools ▸ Device Profile** choice
  from code. While it is not Auto, entering Play Mode logs a warning.
- `PageVisibility`: whether the browser tab is hidden (`IsHidden`, `Changed`,
  `IsSupported`), from `visibilitychange`, `pagehide` and `pageshow`. WebGL only.
- `LongLeapTools.jslib`: primary pointer query and page visibility listener.
- **Tools ▸ WebGL Page Mode ▸ Mobile Pixel Ratio 1** (on by default): uncommenting
  `config.devicePixelRatio = 1` in the default template, which used to be always on,
  can now be turned off.
- The "rotate your device" overlay text follows the browser language: en, ru, uk,
  de, fr, es, pt, it, pl, tr, ja, ko, zh; otherwise English.
- `WebGlPageHtml`: the `index.html` edits as pure, tested functions.
- EditMode tests (`LongLeap.Tools.Tests`): `DeviceInfo`, `RuntimeUiTheme`, `WebGlPageHtml`.

### Changed

- In the browser "mobile" is decided by the primary pointer
  (`(hover: none) and (pointer: coarse)`), the same signal as the orientation
  overlay: a touchscreen laptop is now `WebDesktop`, an iPad with a desktop user
  agent is `WebMobile`. The touchscreen is the fallback when the browser does not answer.
- Docs and code comments are in English.

### Fixed

- `screen.orientation.lock` in fullscreen no longer breaks the page in browsers that
  return nothing or throw instead of a rejected promise.
- A Cyrillic letter in a `RuntimeUi` code comment.

## [0.1.0] - 2026-10-28

First release: `DeviceProfile` / `DeviceInfo` (Desktop, Mobile, WebDesktop,
WebMobile) with `Override` and the **Tools ▸ Device Profile** menu; `RuntimeUi`
(assembly `LongLeap.Tools.UI`) — code-built text, background, slider, button and
layout rows for `com.longleap.settings` and `com.longleap.ui`; the
**Tools ▸ WebGL Page Mode** editor tool (Fixed / Free Aspect, orientation, "rotate
your device" overlay, `devicePixelRatio = 1` on phones).
