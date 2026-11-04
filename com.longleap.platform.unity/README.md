# LongLeap Platform Unity

Platform adapter layer — Unity-specific implementations of abstract contracts defined in upstream LongLeap packages. Zero business logic; purely bridges interfaces to Unity APIs.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `UnityPlayerPrefsAdapter` | Sealed class `: IPlayerPrefs` | Wraps `UnityEngine.PlayerPrefs` static into `IPlayerPrefs` interface |
| `PlayerPrefsPersistentStorage` | Sealed class `: IPersistentStorage` | Key-value storage backed by `PlayerPrefs` |
| `FilePersistentStorage` | Sealed class `: IPersistentStorage` | Key-value storage backed by filesystem; atomic writes via temp file |
| `ResourcesAchievementProvider` | Sealed class `: IAchievementProvider` | Loads achievement definitions from `Resources/Achievements/` |
| `JsonAchievementProvider` | Sealed class `: IAchievementProvider` | Parses manifest + achievement JSON from any text source |

## Usage

### UnityPlayerPrefsAdapter

```csharp
IPlayerPrefs prefs = new UnityPlayerPrefsAdapter();
prefs.SetString("last_level", "forest");
string level = prefs.GetString("last_level", "default_level");
```

### PlayerPrefsPersistentStorage

```csharp
IPersistentStorage storage = new PlayerPrefsPersistentStorage();
await storage.SaveAsync("settings", json);
string data = await storage.LoadAsync("settings"); // null if missing
```

### FilePersistentStorage

```csharp
IPersistentStorage storage = new FilePersistentStorage(Application.persistentDataPath + "/saves");
await storage.SaveAsync("profile", json);
```

Operations run on the thread pool via `Task.Run`. Writes use `temp -> delete -> move` for atomicity.

### ResourcesAchievementProvider

```csharp
IAchievementProvider provider = new ResourcesAchievementProvider(); // Resources/Achievements/
AchievementDefinition[] achievements = await provider.LoadAsync();
```

Reads `manifest.json` and the files it lists via `Resources.Load<TextAsset>`. The JSON ships inside the build — no HTTP requests, so it works on portals that rehost the build (e.g. GamePix). Completes synchronously; call from the main thread. A missing or corrupt manifest is logged as an error: an empty definition set silently ungates everything gated by achievements.

### JsonAchievementProvider

```csharp
IAchievementProvider provider = new JsonAchievementProvider(fileName => LoadText(fileName));
```

Same manifest/file format from any text source (`fileName → text`, `null` if missing). Used by `ResourcesAchievementProvider` and by tests.

## Internals

### Adapter philosophy

Each class implements exactly one upstream interface and delegates to a single Unity concept:

| Class | Implements | Delegates to |
|---|---|---|
| `UnityPlayerPrefsAdapter` | `IPlayerPrefs` | `UnityEngine.PlayerPrefs` |
| `PlayerPrefsPersistentStorage` | `IPersistentStorage` | `UnityEngine.PlayerPrefs` |
| `FilePersistentStorage` | `IPersistentStorage` | `System.IO.File` |
| `ResourcesAchievementProvider` | `IAchievementProvider` | `Resources.Load<TextAsset>` |
