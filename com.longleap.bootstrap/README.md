# LongLeap Bootstrap

Startup infrastructure for a LongLeap game: what runs, in which order, and where the
resulting services live. The steps themselves are game code.

| Type | What it is |
|---|---|
| `ILoadingTask` | One startup step: `ExecuteAsync(LoadingContext)` and `OnError` (return `true` to skip the step and keep loading, `false` to stop). |
| `LoadingPipeline` | Runs the tasks strictly one after another, logs each step with its time. A step may rely on everything registered before it. |
| `LoadingContext` | What a step gets: the service container and the cancellation token. |
| `ServiceContainer` / `IServiceContainer` | Services by contract type. Disposes `IDisposable` services in reverse registration order. |
| `GameServices` | Static access to the container for scene objects. The entry point publishes it with `GameServices.Bind`. |
| `OptionalAddressable` | Load an Addressable that may be missing: absent key → `null` and a warning, not an exception. |
| `LoadSceneTask` | Load a scene under the current screen and activate it once ready. |

## Using it

The game keeps its own entry point (a `MonoBehaviour` created before the first scene)
and its own boot sequence:

```csharp
var services = new ServiceContainer();
GameServices.Bind(services);

var pipeline = new LoadingPipeline()
    .Add(new GameConfigTask())
    .Add(new LoadSceneTask("Game"))
    .Add(new MainMenuTask());

await pipeline.RunAsync(services, cancellationToken);
```

On shutdown dispose the container and call `GameServices.Unbind(services)`.

Loading is frame-by-frame (`Task.Yield`), no threads, so it is WebGL-safe.

## Not here

Tasks that build feature services (stats, achievements, rewards, quests, Xsolla ID) stay
in the game: what to construct and how to wire it is the game's decision.
