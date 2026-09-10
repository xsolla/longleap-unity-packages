using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Notifications
{
    /// <summary>
    /// Manages a priority queue of notifications and raises <see cref="OnShowRequest"/>
    /// events when a notification is ready to be displayed.
    /// </summary>
    public sealed class NotificationService : INotificationService
    {
        private const int MaxRecommendedQueueSize = 10;

        /// <summary>
        /// Raised when a notification is ready to be displayed. The <see cref="Action"/> callback
        /// should be invoked once the notification has been dismissed, which triggers the next
        /// queued notification (if any).
        /// </summary>
        public event Action<NotificationData, Action> OnShowRequest;

        private readonly List<NotificationData> _items = new List<NotificationData>();
        private bool _isBusy;

        /// <summary>
        /// Shows the specified notification immediately if the service is idle, or enqueues it
        /// if a notification is currently being displayed.
        /// </summary>
        /// <param name="data">The notification data to show.</param>
        public void Show(NotificationData data)
        {
            if (OnShowRequest == null)
            {
                Debug.LogError("Cannot show notification: no subscribers registered for OnShowRequest.");
                return;
            }

            if (_isBusy)
            {
                Enqueue(data);
                return;
            }

            _isBusy = true;
            OnShowRequest(data, OnViewComplete);
        }

        /// <summary>
        /// Clears all queued notifications without affecting the currently displayed notification.
        /// </summary>
        public void ClearPending()
        {
            _items.Clear();
        }

        private void OnViewComplete()
        {
            _isBusy = false;
            var next = Dequeue();
            if (next.HasValue)
            {
                Show(next.Value);
            }
        }

        private void Enqueue(NotificationData data)
        {
            int index = 0;
            while (index < _items.Count && data.Priority <= _items[index].Priority)
            {
                index++;
            }

            _items.Insert(index, data);

            if (_items.Count > MaxRecommendedQueueSize)
            {
                Debug.LogWarning($"Notification queue has {_items.Count} items, exceeding the recommended limit of {MaxRecommendedQueueSize}.");
            }
        }

        private NotificationData? Dequeue()
        {
            if (_items.Count == 0)
            {
                return null;
            }

            var result = _items[0];
            _items.RemoveAt(0);
            return result;
        }
    }
}
