# LongLeap Notifications

Priority-queued notification popup system. Displays one notification at a time, draining the queue in priority order. Higher-priority notifications interrupt or queue ahead of lower ones. Zero dependencies.

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
| `NotificationService` | Class (sealed) | Implementation. Busy/idle state machine. Inserts into sorted queue by descending priority when busy. Dequeues next on completion. |
| `NotificationData` | Struct (readonly) | Payload: `Title`, `Message` (default `""`), `Duration` (default 3s), `Priority` (default `Default`). Two constructors: full + title-only. |
| `NotificationPriority` | Enum | `Low` (0), `Default` (10), `High` (20), `Critical` (30) |
| `ITimer` | Interface | Timer seam: `Start(TimeSpan, Action)`, `Cancel()`. Inject custom timers for testing or alternative timing. |
| `CoroutineTimer` | Class (sealed) `: ITimer` | Default timer: a coroutine on a host MonoBehaviour with `WaitForSecondsRealtime` (ignores `Time.timeScale`). |
| `NotificationSystemView` | Abstract class (MonoBehaviour) | Base view. Provides public `OnShowRequest(NotificationData, Action)` handler method. Manages display timer, safety timeout, calls `Complete()` when dismissed. Override `OnShow(NotificationData)` to instantiate popups. |

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

The view **must** be a MonoBehaviour. Subclass `NotificationSystemView` and override three methods:

```csharp
public class MyNotificationView : NotificationSystemView
{
    [SerializeField] private MonoBehaviour popupPrefab;

    protected override void OnShow(NotificationData data)
    {
        var popup = Instantiate(popupPrefab, transform);
        ActivePopup = popup.gameObject;
        popup.GetComponent<IMyPopup>().Show(data, Complete);
    }

    protected override ITimer CreateDisplayTimer() => new CoroutineTimer(this);

    protected override ITimer CreateSafetyTimer() => new CoroutineTimer(this);
}
```

The two timer factory methods (`CreateDisplayTimer()`, `CreateSafetyTimer()`) are **abstract** — subclasses *must* override them. `CoroutineTimer` (in this package) is the usual choice; supply your own `ITimer` for other timing.

### Wiring

```csharp
service.OnShowRequest += GetComponent<MyNotificationView>().OnShowRequest;
```

This is the only coupling point — the service fires an event, the view handles it. No reference in either direction.

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
- **If idle** (no notification currently displayed): the notification is shown immediately.
- **If busy** (a notification is active): the new notification is inserted into the queue in **descending priority order**. Within equal priority, FIFO order is maintained.

### Clearing pending

```csharp
service.ClearPending(); // cancels all queued items, does NOT affect the active one
```

### No subscriber

If `Show()` is called when `OnShowRequest` has no subscribers, an error is logged and the notification is dropped. Always wire a view before calling `Show`.

### Queue size warning

When the pending queue exceeds 10 items, a warning is logged. Items are never dropped due to queue size — it's purely advisory.

### View lifecycle

```
OnShowRequest(data, onComplete)
  → OnShow(data)                   // derived class instantiates popup
  → DisplayTimer.Start(duration)   // duration <= 0 → skips to OnDisplayEnd immediately
  → OnDisplayEnd()                 // timer fires
    → SafetyTimer.Start(3s)        // if popup hangs, safety net fires Complete()
    → derived class animates out, calls Complete()
      → service callback invoked
      → next queued notification dequeued and shown (if any)
```

### Safety timeout

The safety timeout is **3 seconds** (overridable via `protected virtual TimeSpan SafetyTimeout`). If `Complete()` is not called within `3s + display duration`, the safety routine destroys `ActivePopup` and force-completes. This prevents the notification system from deadlocking if a popup's hide animation hangs.

`OnDestroy()` also calls `Complete()` as a safety net for scene unload.

### Custom popup implementation

The package does not ship a popup prefab. Implement your own:

```csharp
public class MyPopup : MonoBehaviour
{
    public void Show(NotificationData data, Action onHidden)
    {
        // populate UI from data.Title, data.Message
        // play show animation
        // schedule hide after data.Duration
        onHidden(); // call when fully hidden
    }
}
```

## Internals

### State machine

```
IDLE (_isBusy=false) ──Show()──→ BUSY (_isBusy=true) ──OnViewComplete()──→ IDLE
                                       │
                                       ├── Show() while busy → ENQUEUE (sorted by priority)
                                       └── OnViewComplete() → Dequeue → Show(next) or IDLE
```

### Queue ordering — Enqueue

```csharp
// Insertion sort: scans forward until it finds a LOWER priority
while (index < items.Count && data.Priority <= items[index].Priority)
    index++;
items.Insert(index, data);
```

Result: `[Critical, Critical, High, High, Default, Low]` — same priority = FIFO.

### Timer implementation

`CoroutineTimer` runs a coroutine on its host MonoBehaviour and waits with `WaitForSecondsRealtime`, so timers are **unscaled** — they continue during `Time.timeScale = 0`. Behaviour:

- `Start` cancels a running countdown first and is safe to call from inside the callback.
- A negative duration counts as zero; the callback still runs no earlier than the next frame.
- A null host (constructor) or null callback throws `ArgumentNullException`; starting on a destroyed host or an inactive GameObject throws `InvalidOperationException`. `NotificationSystemView` catches that for the display timer and completes the notification.
- Unity stops coroutines when the host GameObject is deactivated or destroyed, so a running countdown then never fires. Keep the view active while notifications are shown.

The `ITimer` interface exists so tests can inject `FakeTimer` (a simple callback holder) and manually fire events without waiting for real time. `CoroutineTimer` itself is covered by PlayMode tests.
