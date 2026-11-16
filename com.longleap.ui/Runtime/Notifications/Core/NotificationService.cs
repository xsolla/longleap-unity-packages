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
        /// queued notification (if any). Subscribing while idle immediately shows the next queued notification.
        /// </summary>
        public event Action<NotificationData, Action> OnShowRequest
        {
            add
            {
                _showRequest += value;
                ShowNextIfIdle();
            }
            remove => _showRequest -= value;
        }

        private readonly List<NotificationData> _items = new List<NotificationData>();
        private Action<NotificationData, Action> _showRequest;
        private bool _isBusy;
        private int _showId;

        /// <summary>
        /// Shows the specified notification immediately if the service is idle, or enqueues it
        /// if a notification is currently being displayed or no view is subscribed yet.
        /// </summary>
        /// <param name="data">The notification data to show.</param>
        public void Show(NotificationData data)
        {
            if (_isBusy || _showRequest == null)
            {
                Enqueue(data);
                return;
            }

            StartShow(data);
        }

        /// <summary>
        /// Clears all queued notifications without affecting the currently displayed notification.
        /// </summary>
        public void ClearPending()
        {
            _items.Clear();
        }

        private void StartShow(NotificationData data)
        {
            _isBusy = true;
            var showId = ++_showId;

            try
            {
                _showRequest(data, () => OnViewComplete(showId));
            }
            catch (Exception e)
            {
                Debug.LogError($"NotificationService: OnShowRequest handler threw while showing \"{data.Title}\": {e}");

                // The handler may have completed the notification before throwing; only recover if it did not.
                if (IsCurrentShow(showId))
                {
                    FinishCurrentShow();
                }
            }
        }

        private void OnViewComplete(int showId)
        {
            if (!IsCurrentShow(showId))
            {
                Debug.LogWarning("NotificationService: ignored a duplicate or stale completion callback.");
                return;
            }

            FinishCurrentShow();
        }

        private bool IsCurrentShow(int showId)
        {
            return _isBusy && showId == _showId;
        }

        private void FinishCurrentShow()
        {
            _isBusy = false;
            ShowNextIfIdle();
        }

        private void ShowNextIfIdle()
        {
            // With no subscriber the queue is kept intact until a view subscribes again.
            if (_isBusy || _showRequest == null)
            {
                return;
            }

            var next = Dequeue();
            if (next.HasValue)
            {
                StartShow(next.Value);
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
