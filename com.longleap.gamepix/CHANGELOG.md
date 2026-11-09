# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `GAMEPIX_JS` define, with the **GamePix / Optional / Xsolla ID via JS Bridge
  (GAMEPIX_JS)** menu item and the matching Build Manager option. It selects the
  Xsolla ID sign-in path: on — `XsollaIdAuthJS` (own jslib bridge to
  `window.GamePix.user`), off — `XsollaIdAuthGamePix` (the plugin's `Gpx.User`).
- Build Manager warnings: an error when `GAMEPIX_JS` is off but the plugin has no
  user API (`Assets/Plugins/GamePix/User`), info when `GAMEPIX_JS` is on although it has one.
- README and this changelog.
- `GamePixPluginImportWatcher`: restores the `GAMEPIX` constraint in the plugin's
  asmdefs automatically after every plugin import (a plugin update used to leave a
  plain WebGL build pulling the plugin in until the menu item was clicked).
- `CONTEXT.md`.

### Changed

- **Breaking:** `XsollaIdAuthJS`, `GamePixAuthPayload` and their tests compile only with
  `GAMEPIX_JS`; `XsollaIdAuthGamePix` compiles only without it. Projects that use
  `XsollaIdAuthJS` must turn `GAMEPIX_JS` on.
- All comments, log messages, option tooltips and dialogs are now in English. Editor log
  prefixes now name the class that writes them (`GamePixOptions`,
  `GamePixAsmdefConstraints`) instead of `GamePixPluginMenu`.
- `package.json` description lists everything the package contains.
- The editor assembly now references `LongLeap.BuildManager.Options` (from
  `com.longleap.buildmanager`, new dependency) instead of `LongLeap.Core.Editor`,
  which moved there.

## [0.1.0]

- Initial version: `GamePixAdsProvider`, `GamePixPortalLocale`, `XsollaIdAuthGamePix`,
  `XsollaIdAuthJS`, GamePix menu and Build Manager options, build target,
  `index.html` postprocess, HTML5 zip packager.
