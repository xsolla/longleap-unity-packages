using System;
using UnityEngine;

namespace LongLeap.Notifications
{
    public abstract class NotificationSystemView : MonoBehaviour
    {
        private ITimer _displayTimer;
        private ITimer _safetyTimer;
        private Action _onComplete;
        private bool _completed;
        private int _showId;
        private INotificationService _service;
        private bool _isSubscribed;

        protected ITimer DisplayTimer
        {
            get => _displayTimer ??= CreateDisplayTimer();
            set => _displayTimer = value;
        }

        protected ITimer SafetyTimer
        {
            get => _safetyTimer ??= CreateSafetyTimer();
            set => _safetyTimer = value;
        }

        protected GameObject ActivePopup { get; set; }

        protected virtual TimeSpan SafetyTimeout => TimeSpan.FromSeconds(3);

        /// <summary>
        /// Attaches this view to <paramref name="service"/>. The view stays subscribed only while it is
        /// enabled, and detaches itself on disable/destroy, so the service never targets a dead view.
        /// </summary>
        public void Bind(INotificationService service)
        {
            if (ReferenceEquals(_service, service))
                return;

            Unbind();
            _service = service;

            if (isActiveAndEnabled)
                Subscribe();
        }

        /// <summary>
        /// Detaches this view from its service. A notification already on screen plays out normally.
        /// </summary>
        public void Unbind()
        {
            Unsubscribe();
            _service = null;
        }

        public void OnShowRequest(NotificationData data, Action onComplete)
        {
            if (_onComplete != null)
            {
                Debug.LogError("NotificationSystemView: OnShowRequest called while a notification is already active.");
                return;
            }

            _completed = false;
            _onComplete = onComplete;
            var showId = ++_showId;

            try
            {
                OnShow(data);

                // OnShow may have completed this notification synchronously, in which case the service
                // can already be showing the next one. Don't touch the timers on its behalf.
                if (showId != _showId || _completed)
                    return;

                if (data.Duration <= TimeSpan.Zero)
                {
                    OnDisplayEnd();
                    return;
                }

                DisplayTimer.Start(data.Duration, OnDisplayEnd);
            }
            catch (Exception e)
            {
                Debug.LogError($"NotificationSystemView: failed to show notification: {e}");

                if (showId == _showId && !_completed)
                {
                    DestroyActivePopup();

                    // Complete() clears _onComplete before invoking it, so the service may re-enter
                    // OnShowRequest with the next queued notification from inside the callback.
                    Complete();
                }
            }
        }

        protected abstract void OnShow(NotificationData data);

        protected virtual void OnDisplayEnd()
        {
            SafetyTimer.Start(SafetyTimeout, OnSafetyExpired);
        }

        protected void Complete()
        {
            if (_completed)
                return;

            _completed = true;

            // Use the fields, not the lazy properties: a throwing timer factory must not prevent completion.
            _displayTimer?.Cancel();
            _safetyTimer?.Cancel();

            var callback = _onComplete;
            _onComplete = null;
            callback?.Invoke();
        }

        protected abstract ITimer CreateDisplayTimer();

        protected abstract ITimer CreateSafetyTimer();

        protected void OnSafetyExpired()
        {
            DestroyActivePopup();

            if (!_completed)
            {
                Complete();
            }
        }

        protected virtual void DestroyPopup(GameObject popup)
        {
            Destroy(popup);
        }

        // Derived classes overriding the lifecycle methods below must call the base implementation.

        protected virtual void OnEnable()
        {
            Subscribe();
        }

        protected virtual void OnDisable()
        {
            // Unsubscribe first, so completing the active notification doesn't push the next one into
            // a disabled view (its timers can't run). The service keeps it queued until a view subscribes.
            Unsubscribe();
            AbortActive();
        }

        protected virtual void OnDestroy()
        {
            Unbind();
            AbortActive();
        }

        private void Subscribe()
        {
            if (_service == null || _isSubscribed)
                return;

            // Set before subscribing: the service may show a queued notification from inside the subscription.
            _isSubscribed = true;
            _service.OnShowRequest += OnShowRequest;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
                return;

            _isSubscribed = false;
            _service.OnShowRequest -= OnShowRequest;
        }

        private void AbortActive()
        {
            if (_onComplete == null)
                return;

            DestroyActivePopup();
            Complete();
        }

        private void DestroyActivePopup()
        {
            if (ActivePopup != null)
                DestroyPopup(ActivePopup);

            ActivePopup = null;
        }
    }
}
