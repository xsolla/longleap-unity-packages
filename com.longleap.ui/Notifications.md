# LongLeap UI — Notifications

Priority-queued notification popup system. Displays one notification at a time, draining the queue in priority order. Higher-priority notifications queue ahead of lower ones; they never interrupt the notification already on screen.

Part of `com.longleap.ui`, split into two assemblies:

| Assembly | Folder | Contents |
|---|---|---|
| `LongLeap.Notifications` | `Runtime/Notifications/Core` | Service, data, `NotificationSystemView` base, `ITimer` + `CoroutineTimer`. No LongLeap dependencies — usable on its own with a custom view. |
| `LongLeap.UI.Notifications` | `Runtime/Notifications` | Ready-made `GameNotificationView`, `NotificationPopup`, and `NotificationBootstrapper` (static `Show`, buffers calls until ready). |

Tests: `Tests/EditMode` (`LongLeap.Notifications.Tests`) and `Tests/PlayMode` (`LongLeap.Notifications.PlayModeTests`, `CoroutineTimer`).

## Data Sources

No external data. Notifications are created in code:

```csharp
new NotificationData("Title", "Message", duration: TimeSpan.FromSeconds(5), priority: NotificationPriority.High)
```

All configuration is per-call — no ScriptableObjects, no resource files.

## Public API

| Type | Kind | Purpose |
|---|---|---|
| `INotificationService` | Interface | `event OnShowRequest(NotificationData, Action)`, `Show(NotificationData)`, `ClearPending()` |
| `NotificationService` | Class (sealed) | Implementation. Busy/idle state machine. Inserts into sorted queue by descending priority when busy or when no view is subscribed. Dequeues next on completion or when a view subscribes. |
| `NotificationData` | Struct (readonly) | Payload: `Title`, `Message` (default `""`), `Duration` (default 3s), `Priority` (default `Default`). Two constructors: full + title-only. |
| `NotificationPriority` | Enum | `Low` (0), `Default` (10), `High` (20), `Critical` (30) |
| `ITimer` | Interface | Timer seam: `Start(TimeSpan, Action)`, `Cancel()`. Inject custom timers for testing or alternative timing. |
| `CoroutineTimer` | Class (sealed) `: ITimer` | Default timer: unscaled coroutine (`WaitForSecondsRealtime`) on a host MonoBehaviour. |
| `NotificationSystemView` | Abstract class (MonoBehaviour) | Base view. `Bind(INotificationService)` / `Unbind()` attach it to a service for as long as it is enabled; `OnShowRequest(NotificationData, Action)` is the handler. Manages display timer, safety timeout, calls `Complete()` when dismissed. Override `OnShow(NotificationData)` to instantiate popups. |

## Initialization

### Service — direct construction

`NotificationService` is a plain C# class with no MonoBehaviour dependency. Create it anywhere:

```csharp
var service = new NotificationService();
```

### DI registration

```csharp
container.Bind<INotificationService>().To<NotificationService>().AsSingle();
```

### View — subclassing

The view **must** be a MonoBehaviour. Subclass `NotificationSystemView`, override `OnShow` and the two timer factories, and usually `OnDisplayEnd` to start the hide animation:

```csharp
public class MyNotificationView : NotificationSystemView
{
    [SerializeField] private MyPopup popupPrefab;

    private MyPopup _popup;

    protected override void OnShow(NotificationData data)
    {
        _popup = Instantiate(popupPrefab, transform);
        ActivePopup = _popup.gameObject;
        _popup.Show(data, Complete);
    }

    protected override void OnDisplayEnd()
    {
        if (_popup != null)
            _popup.Hide();

        base.OnDisplayEnd(); // starts the safety timer
    }

    protected override ITimer CreateDisplayTimer() => new CoroutineTimer(this);

    protected override ITimer CreateSafetyTimer() => new CoroutineTimer(this);
}
```

The two timer factory methods (`CreateDisplayTimer()`, `CreateSafetyTimer()`) are **abstract** — subclasses *must* override them. `new CoroutineTimer(this)` is the usual choice.

For the common case you don't need a subclass at all: `GameNotificationView` + `NotificationPopup` (Animator triggers or a code fade) cover it.

### Wiring

```csharp
GetComponent<MyNotificationView>().Bind(service);
```

`Bind` ties the subscription to the view's lifecycle: the view is subscribed only while it is enabled, and unsubscribes itself on `OnDisable`/`OnDestroy`. Call `Unbind()` to detach explicitly. Subclasses that override `OnEnable`/`OnDisable`/`OnDestroy` must call the base implementation.

Subscribing to `OnShowRequest` manually still works, but then unsubscribing before the view is disabled or destroyed is your responsibility.

## Usage

### Showing notifications

```csharp
service.Show(new NotificationData(
    title: "Reward Claimed",
    message: "Day 3 streak bonus",
    priority: NotificationPriority.High));

service.Show(new NotificationData(
    title: "Milestone Reached"));  // message="", duration=3s, priority=Default
```

### Queue behavior

When `Show()` is called:
- **If idle** (no notification currently displayed) and a view is subscribed: the notification is shown immediately.
- **If busy** (a notification is active) or no view is subscribed: the new notification is inserted into the queue in **descending priority order**. Within equal priority, FIFO order is maintained.

There is **no preemption**: a `Critical` notification queued while a `Low` one is on screen waits until the `Low` one completes, then goes first.

### Clearing pending

```csharp
service.ClearPending(); // cancels all queued items, does NOT affect the active one
```

### No subscriber

If `Show()` is called when `OnShowRequest` has no subscribers, the notification is queued. The same happens to the queue when the view detaches mid-show: pending items are kept. As soon as a view subscribes (`Bind` on an enabled view, or re-enabling a bound view), the highest-priority pending notification is shown. Use `ClearPending()` to discard notifications that should not survive, e.g., a scene change.

### View disabled or destroyed

When a bound view is disabled or destroyed during a show, it unsubscribes first, then destroys `ActivePopup` and completes the active notification. The next notification is not pushed into the dead view; it waits in the queue for the next subscriber.

### Failure handling

- The completion callback passed to `OnShowRequest` is **one-shot per notification**. Calling it twice, or calling a callback from an earlier notification, logs a warning and is ignored — it never advances the queue a second time.
- If an `OnShowRequest` handler throws, the error is logged and the service moves on to the next queued notification instead of staying busy forever. If the handler already invoked the callback before throwing, nothing extra happens.
- `NotificationSystemView` does the same on its side: if `OnShow` throws, the popup in `ActivePopup` is destroyed and the notification is completed. `Complete()` can be called synchronously from `OnShow`.
- Wire **one** view per service. With several subscribers every view would display the notification, and only the first completion counts.

### Queue size warning

When the pending queue exceeds 10 items, a warning is logged. Items are never dropped due to queue size — it's purely advisory.

### View lifecycle

```
OnShowRequest(data, onComplete)
  → OnShow(data)                   // derived class instantiates popup
  → DisplayTimer.Start(duration)   // duration <= 0 → skips to OnDisplayEnd immediately
  → OnDisplayEnd()                 // timer fires; derived class starts the hide animation here
    → SafetyTimer.Start(SafetyTimeout) // if popup hangs, safety net fires Complete()
    → popup finishes hiding, derived class calls Complete()
      → service callback invoked
      → next queued notification dequeued and shown (if any)
```

### Safety timeout

The safety timeout is **3 seconds** (overridable via `protected virtual TimeSpan SafetyTimeout`). If `Complete()` is not called within `3s + display duration`, the safety routine destroys `ActivePopup` and force-completes. This prevents the notification system from deadlocking if a popup's hide animation hangs.

`OnDisable()` and `OnDestroy()` also abort the active notification (destroy `ActivePopup`, call `Complete()`), so a disabled view whose timers can no longer run never blocks the queue.

### Custom popup implementation

The package does not ship a popup prefab (logic only). `NotificationPopup` is the ready-made popup script; for a custom one, note that the view owns the timing (`data.Duration`), so the popup only needs show/hide:

```csharp
public class MyPopup : MonoBehaviour
{
    private Action _onHidden;

    public void Show(NotificationData data, Action onHidden)
    {
        _onHidden = onHidden;
        // populate UI from data.Title, data.Message
        // play show animation
    }

    public void Hide()
    {
        // play hide animation, then:
        _onHidden?.Invoke(); // call when fully hidden
        Destroy(gameObject);
    }
}
```

The view calls `Hide()` from `OnDisplayEnd()` (see `GameNotificationView` / `NotificationPopup`).

## Internals

### State machine

```
IDLE (_isBusy=false) ──Show() + subscriber──→ BUSY (_isBusy=true) ──onComplete()──→ IDLE
  │                                              │
  ├── Show() with no subscriber → ENQUEUE        ├── Show() while busy → ENQUEUE (sorted by priority)
  └── subscriber added → Dequeue → BUSY          ├── onComplete() → Dequeue → BUSY(next), or IDLE if queue empty / no subscriber
                                                 └── stale or repeated onComplete() → ignored (warning)
```

Each show gets an id; the `onComplete` callback handed to the view carries it, which is how stale and repeated callbacks are detected.

### Queue ordering — Enqueue

```csharp
// Insertion sort: scans forward until it finds a LOWER priority
while (index < items.Count && data.Priority <= items[index].Priority)
    index++;
items.Insert(index, data);
```

Result: `[Critical, Critical, High, High, Default, Low]` — same priority = FIFO.

### Timer implementation

Views get their timers by overriding `CreateDisplayTimer()` and `CreateSafetyTimer()`.

`CoroutineTimer` uses `WaitForSecondsRealtime` on a host MonoBehaviour, which means timers are **unscaled** — they continue during `Time.timeScale = 0`. Behaviour:

- `Start` cancels a running countdown first and is safe to call from inside the callback.
- A negative duration counts as zero; the callback still runs no earlier than the next frame.
- A null host (constructor) or null callback throws `ArgumentNullException`; starting on a destroyed host or an inactive GameObject throws `InvalidOperationException`. `NotificationSystemView` catches that for the display timer and completes the notification.
- Unity stops coroutines when the host GameObject is deactivated or destroyed, so a running countdown then never fires. `NotificationSystemView` covers this by aborting the active notification in `OnDisable` / `OnDestroy`.

The `ITimer` interface exists so tests can inject `FakeTimer` (a simple callback holder) and manually fire events without waiting for real time. `CoroutineTimer` itself is covered by PlayMode tests (`Tests/PlayMode`).
