# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-11-04

First release.

### Added

- `FirebaseAnonymousAuthProvider`: anonymous Firebase Authentication over REST
  with a cached token, token refresh and sign-up fallback.
- `FirebaseHttpClient`: Realtime Database REST client that attaches the ID token
  to every request and masks it in error messages.
- `FirebaseCloudSaveProvider`: cloud saves in Realtime Database, with a guest
  scope (`cloud_saves/{uid}`) and an account scope (`xsolla_saves/{accountId}`)
  through `IAccountCloudSaveScope`. The selected scope persists across launches.
- `FirebaseLeaderboardProvider`: submit only-better scores, top-N, player rank
  and surroundings, ordered by `negScore`.
- `FirebaseRemoteConfigProvider`: Firebase Installation ID registration and
  Remote Config REST fetch, including A/B experiment assignments. Supports
  cancellation (aborts the request). Parameter values are kept verbatim
  (ISO date strings are not reformatted in the current culture).
- `FirebaseConfigSO` config asset.
- Injectable HTTP handlers on every network class, for tests.
