# LongLeap Firebase

Production Firebase backend implementations for all LongLeap services. Each backend implements a provider interface defined in `com.longleap.core` (or `com.longleap.remoteconfig`).

**FirebaseAnonymousAuthProvider** — Implements `IAuthProvider`. Anonymous Firebase Auth sign-in.

**FirebaseCloudSaveProvider** — Implements `ICloudSaveProvider`. RTDB read/write with version-based conflict resolution.

**FirebaseLeaderboardProvider** — Implements `ILeaderboardProvider`. RTDB score submission and retrieval with deferred batch writes.

**FirebaseRemoteConfigBackend** — Implements `IRemoteConfigProvider` (from `com.longleap.remoteconfig`). Fetches typed parameters from Firebase Remote Config REST API. Handles FID registration, caching, and A/B experiment payloads.

**FirebaseConfigSO** — ScriptableObject holding `GoogleAppId`, `ApiKey`, `ProjectNumber`.

**FirebaseHttpClient** — Internal utility for Firebase REST API calls with auth token management.

## Relationships

- **Firebase → Core**: Implements `IAuthProvider`, `ICloudSaveProvider`, `ILeaderboardProvider`.
- **Firebase → RemoteConfig**: Implements `IRemoteConfigProvider`; depends on `InMemoryPlayerPrefs` for FID storage.
