# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-11-02

First release.

### Added

- **Sign-in** (`LongLeap.XsollaId`): Xsolla ID sign-in via OAuth2/OIDC
  authorization code + PKCE (S256) behind `IXsollaIdAuth`, with silent session
  restore from a stored refresh token, token refresh and sign-out. Failures are
  returned as `XsollaIdResult` with an `XsollaIdErrorCode`; nothing is thrown to
  the caller.
- `XsollaIdConfigSO` config asset with separate Stage and Production client
  registrations, scopes and an allowed game origins list.
- `OriginGuard` and `XsollaIdRedirect`: origin checks before opening the
  sign-in window, and a redirect_uri derived from the page address for
  self-hosted builds.
- `PlayerPrefsXsollaIdSessionStore`: session persistence under its own
  PlayerPrefs key, separate from the Firebase anonymous identity.
- `UnavailableXsollaIdAuth` and `UnavailableXsollaIdAvatarLoader` stubs, so game
  code needs no `#if` when sign-in is off.
- **Web transport** (`LongLeap.XsollaId.Web`, gated by `XSOLLA_ID`): OIDC
  discovery with caching and warm-up, token client (code exchange, refresh,
  userinfo, best-effort revocation), WebGL popup bridge (`XsollaId.jslib`),
  browser-side avatar decoding (WEBP and other formats), and `XsollaIdFactory`.
- RewardService providers for an Xsolla ID account: cloud saves
  (`XsollaCloudSaveProvider`, `XsollaCloudSaveScope`), leaderboards
  (`XsollaLeaderboardProvider`, `XsollaAccountAuthProvider`) and leagues
  (`XsollaLeagueProvider`).
- **Account widget** (`LongLeap.XsollaId.UI`): `XsollaIdAccountWidget` prefab
  in the Xsolla brand style with checking, signed-out and signed-in states, an
  expandable account card, a Backpack link and localized labels; editor
  previews via `FakeXsollaIdAuth`.
- `XsollaIdDebugPanel` on-screen debug panel behind `XSOLLA_ID_DEBUG`.
- **Editor**: `Xsolla ID` menu (define toggles, environment switch, config
  selection), Build Manager options and warnings, and a post-build step that
  copies `xid-callback.html` into the WebGL build.
- **Startup account flow** (`LongLeap.XsollaId.Accounts`): `AccountGate`
  decides who plays this launch — an Xsolla ID account or a guest — before the
  save service starts: waits for the silent session restore, shows the
  game-drawn startup screen (`IAccountScreen`) only when there is no session,
  reads the account save, and offers "Try again" or "Play as guest" when that
  read fails. The guest choice lasts for the app session
  (`AccountGate.ForgetGuestChoice()` clears it).
- `SaveSwitch`: brings the local save mirror in line with this launch's owner
  before `CloudSaveService.InitializeAsync` — clears another owner's local
  save, uploads a local save the server lost, merges guest progress into the
  account. Switches the scope only after the mirror holds the new owner's
  data, so a failure is retried on the next launch. Reports
  `SaveSwitchOutcome` (`IsFailure()`: play the launch on a local-only save) and
  never throws. `new SaveSwitch(saves, scope, rules)` takes the
  `CloudSaveService` once.
- `SaveMergeRules`: per-key merge table with glob patterns and built-in rules
  (`PreferAccount`, `PreferLocal`, `Max`, `Min`, `Or`, `Union`) or custom
  functions.
- EditMode tests for the runtime, web, UI and accounts assemblies.
