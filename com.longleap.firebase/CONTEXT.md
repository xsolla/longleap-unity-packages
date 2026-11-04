# LongLeap Firebase

Production Firebase backends for LongLeap services, pure REST, no Firebase SDK. Each backend implements a provider interface defined in `com.longleap.core` (or `com.longleap.remoteconfig`).

**FirebaseAnonymousAuthProvider** — Implements `IAuthProvider`. Anonymous Firebase Auth: cached token → refresh → sign-up. Caches tokens in `UnityEngine.PlayerPrefs` (`longleap_auth`).

**FirebaseCloudSaveProvider** — Implements `ICloudSaveProvider` and `IAccountCloudSaveScope`. RTDB key/value storage. Guest saves at `cloud_saves/{uid}`, account saves at `xsolla_saves/{accountId}`; the selected scope is persisted in `IPlayerPrefs`. No conflict resolution here; values are `CloudSaveEnvelope` strings from core.

**FirebaseLeaderboardProvider** — Implements `ILeaderboardProvider`. RTDB score submission and retrieval. Submits are immediate, written only when they beat the stored score; queries order by `negScore` (needs `.indexOn`).

**FirebaseRemoteConfigProvider** — Implements `IRemoteConfigProvider` (from `com.longleap.remoteconfig`). Registers a Firebase Installation ID (stored in `IPlayerPrefs`), then fetches parameters and A/B experiment payloads from the Remote Config REST API. Parameter caching is done by `com.longleap.remoteconfig`, not here.

**FirebaseConfigSO** — ScriptableObject holding `ApiKey`, `DatabaseUrl`, `GoogleAppId`, `ProjectNumber`.

**FirebaseHttpClient** — Public RTDB REST client shared by the cloud save and leaderboard providers. Attaches a fresh ID token from `IAuthProvider` to every request.

## Relationships

- **Firebase → Core**: Implements `IAuthProvider`, `ICloudSaveProvider`, `IAccountCloudSaveScope`, `ILeaderboardProvider`; uses `IPlayerPrefs` and `CloudSaveEnvelope`.
- **Firebase → RemoteConfig**: Implements `IRemoteConfigProvider`. Tests use `InMemoryPlayerPrefs` from `com.longleap.core`.
- **XsollaId → Firebase**: `com.longleap.xsollaid` drives `IAccountCloudSaveScope` on sign-in/sign-out and shares the `longleap_cloudsave_scope` prefs key.
