using System;

namespace LongLeap.Notifications
{
    /// <summary>
    /// Represents a notification payload with title, message, display duration, and priority.
    /// </summary>
    public readonly struct NotificationData
    {
        /// <summary>
        /// Gets the notification title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the notification message body.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the display duration.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Gets the priority level.
        /// </summary>
        public NotificationPriority Priority { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="NotificationData"/> with all properties.
        /// </summary>
        /// <param name="title">The notification title.</param>
        /// <param name="message">The notification message body. Defaults to empty.</param>
        /// <param name="duration">Display duration. Defaults to 3 seconds.</param>
        /// <param name="priority">Priority level. Defaults to <see cref="NotificationPriority.Default"/>.</param>
        public NotificationData(string title, string message = "", TimeSpan? duration = null, NotificationPriority priority = NotificationPriority.Default)
        {
            Title = title;
            Message = message;
            Duration = duration ?? TimeSpan.FromSeconds(3);
            Priority = priority;
        }

        /// <summary>
        /// Initializes a new instance of <see cref="NotificationData"/> with only a title.
        /// Message defaults to empty, duration to 3 seconds, priority to Default.
        /// </summary>
        /// <param name="title">The notification title.</param>
        public NotificationData(string title) : this(title, "", null, NotificationPriority.Default)
        {
        }
    }
}
