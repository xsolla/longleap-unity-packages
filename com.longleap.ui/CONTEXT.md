# LongLeap UI

Window system and ready-made windows for LongLeap features. See `README.md`.

## Notifications

Priority-queued notification popups. The core (`LongLeap.Notifications` assembly, `Runtime/Notifications/Core`) has no dependencies on other LongLeap packages; `LongLeap.UI.Notifications` adds the game-ready view, popup, and bootstrapper. Details: `Notifications.md`.

**Notification**:
A popup shown to the player. Has a title, message text, display duration, and a **priority**. Only one notification is on screen at a time; the rest wait in the queue. Used for achievement unlocks, daily rewards, and daily quest completions.

**Priority**:
Queue ordering level: `Critical` > `High` > `Default` > `Low`. A higher priority only moves a notification ahead in the queue — it never interrupts the one already on screen. Equal priorities are shown in FIFO order.

**NotificationData**:
Readonly struct: `Title`, `Message` (default `""`), `Duration` (default 3s), `Priority` (default `Default`).

**INotificationService**:
Interface: `event OnShowRequest(NotificationData, Action onComplete)`, `Show(NotificationData)`, `ClearPending()`.

**NotificationService**:
Concrete implementation. Busy/idle state machine over a priority-sorted `List` (linear insert). Raises `OnShowRequest` for the next notification and waits for its one-shot `onComplete` callback before showing the following one. With no subscriber, notifications stay queued until a view subscribes.

**NotificationSystemView**:
Abstract MonoBehaviour that renders notifications. Attached to a service with `Bind(service)`; it is subscribed only while enabled and unsubscribes on disable/destroy. Runs the display timer and the safety timeout, and calls `Complete()` to hand control back to the service. Subclasses provide the visuals (`OnShow`) and the timers (`CreateDisplayTimer`, `CreateSafetyTimer`). `GameNotificationView` is the ready-made subclass.

**ITimer**:
One-shot timer seam (`Start`, `Cancel`) used by the view. `CoroutineTimer` is the default implementation (unscaled coroutine on a host MonoBehaviour); tests use a fake.

**NotificationBootstrapper**:
Scene component that creates the `NotificationService`, binds `GameNotificationView` to it, and exposes static `Show(title, message)`. Calls made before it is ready are buffered and replayed.
