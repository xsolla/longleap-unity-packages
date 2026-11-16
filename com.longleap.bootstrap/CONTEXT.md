# LongLeap Bootstrap

Startup infrastructure: what runs at launch, in which order, and where the resulting services live. The steps themselves are game code; see README.md for usage.

**Loading task** (`ILoadingTask`, base `LoadingTask`):
One startup step: `ExecuteAsync(LoadingContext)`. `OnError` decides what a failure means — `true` skips the step and keeps loading, `false` stops. `LoadingTask` skips by default; `IsCritical => true` makes it stop instead. Avoid: *Step*, *Job*, *Initializer*.

**Loading pipeline** (`LoadingPipeline`):
Runs tasks strictly one after another; a task may rely on everything registered before it. Each task has a **weight** — its share of the progress bar. Logs every step with its time and a summary at the end. Only cancellation of the token passed to `RunAsync` counts as "cancelled"; any other `OperationCanceledException` is an ordinary step failure.

**Loading context** (`LoadingContext`):
What a task receives: the service container, the cancellation token and `ReportProgress(0..1)`.

**Loading progress** (`LoadingProgress`):
Current step, its own progress and the overall progress weighted by step.

**Service container** (`IServiceContainer` / `ServiceContainer`):
Services by **contract** type. `Register<TContract>(instance)` registers under the contract; without a type argument it registers under the concrete type, and a miss that a registered service could satisfy is reported in the log. Disposes `IDisposable` services in reverse registration order, each instance once. Avoid: *Locator*, *Registry*, *DI*, *ServiceBuilder* (that name in older docs never existed in code).

**GameServices**:
The static access point for scene objects that cannot receive the container by constructor. Published by the entry point with `Bind`, withdrawn with `Unbind`. `TryGet<T>` reads, `WhenAvailable<T>` calls back once the contract is registered (the callback runs inside `Register`, i.e. inside the loading step).

**Optional addressable** (`OptionalAddressable`):
Load an Addressable that may legitimately be missing: absent key → `null` and a warning, not an exception. Loaded handles are released when the container is disposed.

**LoadSceneTask**:
Critical task that loads a scene under the current screen, reports its progress and activates it once ready.

## Invariants

- Loading is frame-based (`Awaitable`), no threads — WebGL-safe.
- An exception thrown by `OnError` itself makes the step critical: both exceptions are logged and loading stops.
- Which services to build, and how to wire them, is the game's decision. Feature-service tasks never move into this package.

## Relationships

- **Bootstrap → Addressables**: `OptionalAddressable` and `LoadSceneTask`. The only dependency.
- **Game → Bootstrap**: the game's entry point builds the pipeline; its tasks construct services from the feature packages (stats, achievements, Xsolla ID…) and register them here.
- **UI → Bootstrap**: none. `com.longleap.ui` windows receive services through `IWindowService.OnCreated`, bound by game tasks.
