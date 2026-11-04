using System;

namespace LongLeap.Notifications
{
    /// <summary>
    /// Service interface for showing and managing priority-queued notifications.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Raised when a notification is ready to be displayed. The <see cref="Action"/> callback
        /// should be invoked once the notification has been dismissed.
        /// </summary>
        event Action<NotificationData, Action> OnShowRequest;

        /// <summary>
        /// Enqueues a notification for display.
        /// </summary>
        /// <param name="data">The notification data to show.</param>
        void Show(NotificationData data);

        /// <summary>
        /// Clears all pending notifications from the queue.
        /// </summary>
        void ClearPending();
    }
}
