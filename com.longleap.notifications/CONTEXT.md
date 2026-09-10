# LongLeap Notifications

Priority-queued notification popups. Standalone package — no dependencies on other LongLeap packages.

**Notification**:
A priority-queued popup shown to the player. Has a title, body text, and a **priority** level. Higher-priority notifications interrupt or queue ahead of lower ones. Used for achievements, daily rewards, and leaderboard milestones.

**Priority**:
An ordering level for notifications: `Critical` (achievement unlocks), `High` (leaderboard milestones), `Default`, `Low`. The notification service drains its queue in priority order.

**NotificationData**:
Readonly struct holding title, body, priority, and display duration.

**INotificationService**:
Interface: `Enqueue(NotificationData)` and `DismissCurrent()`.

**NotificationService**:
Concrete implementation with priority queue (linear insert into `List`), auto-advance on dismiss.

**NotificationSystemView**:
Abstract MonoBehaviour for rendering notifications. Game code extends this to provide the visual implementation.
