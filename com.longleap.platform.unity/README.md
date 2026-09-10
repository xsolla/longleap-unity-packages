# LongLeap Platform Unity

Platform adapter layer — Unity-specific implementations of abstract contracts defined in upstream LongLeap packages. Zero business logic; purely bridges interfaces to Unity APIs.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `UnityPlayerPrefsAdapter` | Sealed class `: IPlayerPrefs` | Wraps `UnityEngine.PlayerPrefs` static into `IPlayerPrefs` interface |
| `PlayerPrefsPersistentStorage` | Sealed class `: IPersistentStorage` | Key-value storage backed by `PlayerPrefs` |
| `FilePersistentStorage` | Sealed class `: IPersistentStorage` | Key-value storage backed by filesystem; atomic writes via temp file |
| `StreamingAssetsAchievementProvider` | Sealed class `: IAchievementProvider` | Loads achievement definitions from `StreamingAssets/Achievements/` |
| `CoroutineTimer` | Sealed class `: ITimer` | One-shot timer via Unity coroutines (`WaitForSecondsRealtime`) |

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

### StreamingAssetsAchievementProvider

```csharp
IAchievementProvider provider = new StreamingAssetsAchievementProvider();
AchievementDefinition[] achievements = await provider.LoadAsync();
```

In Editor uses `File.ReadAllText`; in builds uses `UnityWebRequest`.

### CoroutineTimer

```csharp
ITimer timer = new CoroutineTimer(this);
timer.Start(TimeSpan.FromSeconds(5), () => Debug.Log("Elapsed"));
timer.Cancel();
```

Uses `WaitForSecondsRealtime` — continues during `Time.timeScale = 0`.

## Internals

### Adapter philosophy

Each class implements exactly one upstream interface and delegates to a single Unity concept:

| Class | Implements | Delegates to |
|---|---|---|
| `UnityPlayerPrefsAdapter` | `IPlayerPrefs` | `UnityEngine.PlayerPrefs` |
| `PlayerPrefsPersistentStorage` | `IPersistentStorage` | `UnityEngine.PlayerPrefs` |
| `FilePersistentStorage` | `IPersistentStorage` | `System.IO.File` |
| `StreamingAssetsAchievementProvider` | `IAchievementProvider` | `File.ReadAllText` / `UnityWebRequest` |
| `CoroutineTimer` | `ITimer` | `MonoBehaviour.StartCorotine` |