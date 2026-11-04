# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-11-04

First release. The pre-release builds were marked `1.0.0`; the version is reset
to match the other LongLeap packages.

### Added

- `IRemoteConfigService` / `RemoteConfigService`: typed getters with inline
  defaults (`string`, `int`, `long`, `float`, `double`, `bool`, JSON) in `Get*`
  and non-logging `TryGet*` forms, `HasKey`, `Values`.
- `FetchAsync(CancellationToken)` returning `RemoteConfigFetchStatus`; concurrent
  calls share one request; `Dispose` aborts the request in flight.
- `RemoteConfigOptions`: cache key, deferred activation (`ActivateOnFetch`,
  `Activate()`, `HasPendingValues`), `MinimumFetchInterval`, missing-key
  warnings switch, injectable clock.
- `Updated` event, `LastFetchStatus`, `LastSuccessfulFetchUtc`.
- Versioned PlayerPrefs cache holding parameters, A/B experiments and the fetch
  time.
- `IRemoteConfigProvider` seam with `RemoteConfigFetchResult.Success(...)` /
  `Failure`, and `NullRemoteConfigProvider` for offline builds.

### Changed (from the pre-release builds)

- `NullRemoteConfigBackend` renamed to `NullRemoteConfigProvider`;
  `FirebaseRemoteConfigBackend` (`com.longleap.firebase`) renamed to
  `FirebaseRemoteConfigProvider`.
- `InMemoryPlayerPrefs` moved to `com.longleap.core` (`LongLeap.Core`).
- `RemoteConfigFetchResult` is built through `Success(...)` / `Failure`; its
  collections are never null. A successful result always replaces the whole
  parameter set.
- Missing-key warnings are logged only after values came from the server, and
  parse warnings only once per key and type.
- Booleans accept the Firebase SDK tokens (`yes`, `on`, `t`, ...); integers accept
  whole decimals (`"42.0"`); values are trimmed; `NaN`/`Infinity` are rejected.
- `GetJson<T>` treats a JSON `null` as invalid and returns the default.
- The cache format changed; caches written by the pre-release builds are
  discarded once with a warning.
