# LongLeap Platform Unity

Unity-specific platform implementations for core and package interfaces.

**UnityPlayerPrefsAdapter** — Implements `IPlayerPrefs` using Unity `PlayerPrefs` API.

**FilePersistentStorage** — Implements `IPersistentStorage` using `Application.persistentDataPath` with atomic file writes.

**PlayerPrefsPersistentStorage** — Implements `IPersistentStorage` using Unity `PlayerPrefs` as key-value storage.

**CoroutineTimer** — Implements `ITimer` (from `com.longleap.notifications`) using Unity coroutines.

**ResourcesAchievementProvider** — Implements `IAchievementProvider`. Loads achievement definitions from JSON files in `Resources/Achievements` (inside the build, no network). Parsing is delegated to **JsonAchievementProvider**, which supports multi-file manifests and logs errors on a missing or corrupt manifest.

## Relationships

- **Platform.Unity → Core**: Implements `IPlayerPrefs`, `IPersistentStorage`.
- **Platform.Unity → Achievements**: Implements `IAchievementProvider`.
- **Platform.Unity → Notifications**: Implements `ITimer`.
