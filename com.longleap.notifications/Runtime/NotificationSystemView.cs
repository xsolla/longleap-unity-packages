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

        public void OnShowRequest(NotificationData data, Action onComplete)
        {
            if (_onComplete != null)
            {
                Debug.LogError("NotificationSystemView: OnShowRequest called while a notification is already active.");
                return;
            }

            _completed = false;
            _onComplete = onComplete;

            try
            {
                OnShow(data);

                if (data.Duration <= TimeSpan.Zero)
                {
                    OnDisplayEnd();
                    return;
                }

                DisplayTimer.Start(data.Duration, OnDisplayEnd);
            }
            catch (Exception e)
            {
                Debug.LogError($"NotificationSystemView: ошибка при показе уведомления: {e.Message}");
                _onComplete?.Invoke();
                _onComplete = null;
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
            DisplayTimer.Cancel();
            SafetyTimer.Cancel();

            var callback = _onComplete;
            _onComplete = null;
            callback?.Invoke();
        }

        protected abstract ITimer CreateDisplayTimer();

        protected abstract ITimer CreateSafetyTimer();

        protected void OnSafetyExpired()
        {
            if (gameObject != null && ActivePopup != null)
            {
                DestroyPopup(ActivePopup);
            }

            if (!_completed)
            {
                Complete();
            }
        }

        protected virtual void DestroyPopup(GameObject popup)
        {
            Destroy(popup);
        }

        protected virtual void OnDestroy()
        {
            Complete();
        }
    }
}
