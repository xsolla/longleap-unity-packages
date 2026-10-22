using System.Collections.Generic;
using LongLeap.Notifications;
using UnityEngine;

namespace LongLeap.UI
{
    public class NotificationBootstrapper : MonoBehaviour
    {
        private const int MaxPendingNotifications = 32;

        private static readonly Queue<NotificationData> PendingNotifications = new();
        private static NotificationService _activeService;

        [SerializeField] private GameNotificationView notificationView;

        private NotificationService _service;

        public bool IsReady { get; private set; }

        public NotificationService Service => _service;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PendingNotifications.Clear();
            _activeService = null;
        }

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// Shows a game notification. Calls made before the notification canvas is loaded are queued
        /// and played after <see cref="NotificationBootstrapper"/> becomes ready.
        /// </summary>
        public static void Show(string title, string message = "")
        {
            var data = new NotificationData(title, message);

            if (_activeService != null)
            {
                _activeService.Show(data);
                return;
            }

            if (PendingNotifications.Count >= MaxPendingNotifications)
            {
                PendingNotifications.Dequeue();
                Debug.LogWarning(
                    $"NotificationBootstrapper: pending queue exceeded {MaxPendingNotifications}; the oldest notification was discarded.");
            }

            PendingNotifications.Enqueue(data);
        }

        public void Initialize()
        {
            if (IsReady)
                return;

            if (notificationView == null)
            {
                Debug.LogError("NotificationBootstrapper: notificationView не назначен.", this);
                return;
            }

            _service = new NotificationService();
            _service.OnShowRequest += notificationView.OnShowRequest;

            IsReady = true;
            _activeService = _service;

            while (PendingNotifications.Count > 0)
                _service.Show(PendingNotifications.Dequeue());

            Debug.Log("NotificationBootstrapper: готово.");
        }

        private void OnDestroy()
        {
            if (_service != null && notificationView != null)
                _service.OnShowRequest -= notificationView.OnShowRequest;

            if (ReferenceEquals(_activeService, _service))
                _activeService = null;

            IsReady = false;
            _service = null;
        }
    }
}
