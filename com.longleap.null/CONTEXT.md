# LongLeap Null

Null/stub backend implementations for all LongLeap services. Always returns success (or the safe default). Used for testing, offline mode, and when a backend is optional. Shipped in Runtime — explicitly opted into, never a silent fallback.

**NullAuthProvider** — Implements `IAuthProvider`. Always succeeds with a synthetic `UserId` and `IdToken`.

**NullCloudSaveProvider** — Implements `ICloudSaveProvider`. Always succeeds; stores data in-memory for the session.

**NullLeaderboardProvider** — Implements `ILeaderboardProvider`. Returns empty boards and rank 0.

**NullRemoteConfigBackend** — Implements `IRemoteConfigProvider` (from `com.longleap.remoteconfig`). Returns `IsSuccess=false`, forcing the game to use cached values or inline defaults.

## Relationships

- **Null → Core**: Implements provider interfaces from `com.longleap.core`.
- **Null → RemoteConfig**: Implements `IRemoteConfigProvider` from `com.longleap.remoteconfig`.
