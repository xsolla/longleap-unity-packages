# LongLeap Bootstrap

Startup infrastructure for a LongLeap game: what runs, in which order, and where the
resulting services live. The steps themselves are game code.

| Type | What it is |
|---|---|
| `ILoadingTask` | One startup step: `ExecuteAsync(LoadingContext)` and `OnError` (return `true` to skip the step and keep loading, `false` to stop). |
| `LoadingTask` | Base for a step: write only `ExecuteAsync`. A failure is skipped; override `IsCritical => true` to stop loading instead. |
| `LoadingPipeline` | Runs the tasks strictly one after another, logs each step with its time and a summary at the end, reports `ProgressChanged`. A step may rely on everything registered before it. |
| `LoadingContext` | What a step gets: the service container, the cancellation token and `ReportProgress(0..1)`. |
| `LoadingProgress` | Current step, its progress and the overall progress weighted by step. |
| `ServiceContainer` / `IServiceContainer` | Services by contract type, `Registered` event. Disposes `IDisposable` services in reverse registration order, each instance once. |
| `GameServices` | Static access to the container for scene objects; `WhenAvailable<T>` calls back once a service is registered. The entry point publishes it with `GameServices.Bind`. |
| `OptionalAddressable` | Load an Addressable that may be missing: absent key → `null` and a warning, not an exception. Loaded assets are released when the container is disposed. |
| `LoadSceneTask` | Load a scene under the current screen, report its progress and activate it once ready. Critical. |

## Install (Unity Package Manager)

Unity 6 (6000.0 – 6000.4), WebGL build target. In Unity: **Window → Package Manager → + → Add package from git URL**. This package needs no other LongLeap package.

```
https://github.com/xsolla/longleap-unity-packages.git?path=/com.longleap.bootstrap
```

Pin a version by adding `#v1.0.0` at the end of each URL. Unity packages this one uses are installed automatically. All packages and their dependencies: [root README](../README.md#install-unity-package-manager).

## Using it

The game keeps its own entry point (a `MonoBehaviour` created before the first scene)
and its own boot sequence:

```csharp
var services = new ServiceContainer();
GameServices.Bind(services);

var pipeline = new LoadingPipeline()
    .Add(new GameConfigTask())
    .Add(new LoadSceneTask("Game"), weight: 5f)   // weight: share of the progress bar
    .Add(new MainMenuTask());

pipeline.ProgressChanged += progress => loadingBar.Value = progress.Value;
await pipeline.RunAsync(services, cancellationToken);
```

On shutdown dispose the container and call `GameServices.Unbind(services)`.

A step:

```csharp
public sealed class AudioServiceTask : LoadingTask
{
    public override async Task ExecuteAsync(LoadingContext context)
    {
        var library = await OptionalAddressable.LoadAsync<AudioLibrary>(AudioLibrary.AddressableKey, context);
        context.Services.Register<IAudioService>(new AudioService(library));
    }
}
```

Register under the contract callers ask for: `Register(audio)` without a type argument
registers under the concrete type. When a lookup misses but a registered service implements
the contract, the container says so in the log.

A scene object that may wake up before loading reaches its service:

```csharp
private IDisposable _playerWait;

private void Start() => _playerWait = GameServices.WhenAvailable<IPlayerService>(Bind);
private void OnDestroy() => _playerWait?.Dispose();
```

The callback runs inside `Register`, i.e. inside the loading step; its exceptions are logged
and do not fail the step.

## Cancellation and errors

Only a cancellation of the token passed to `RunAsync` stops loading as "cancelled". An
`OperationCanceledException` from anything else (a request timeout inside a step) is an
ordinary failure of that step and goes through `OnError`. If `OnError` itself throws, the
step is treated as critical: both exceptions are logged and loading stops.

The log ends with a summary: total time, time since application start, how many steps
succeeded and which were skipped.

Loading is frame-by-frame (`Awaitable`), no threads, so it is WebGL-safe.

## Not here

Tasks that build feature services (stats, achievements, rewards, quests, Xsolla ID) stay
in the game: what to construct and how to wire it is the game's decision.
