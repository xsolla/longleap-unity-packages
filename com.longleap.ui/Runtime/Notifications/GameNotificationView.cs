
using LongLeap.Notifications;
using LongLeap.Platform.Unity;
using UnityEngine;

namespace LongLeap.UI
{
    public class GameNotificationView : NotificationSystemView
    {
        [SerializeField] private NotificationPopup popupPrefab;

        private NotificationPopup _currentPopup;

        protected override void OnShow(NotificationData data)
        {
            if (popupPrefab == null)
            {
                Debug.LogError("GameNotificationView: popupPrefab is not assigned.", this);
                Complete();
                return;
            }

            _currentPopup = Instantiate(popupPrefab, transform);
            ActivePopup = _currentPopup.gameObject;

            _currentPopup.Show(data, Complete);
        }

        protected override void OnDisplayEnd()
        {
            if (_currentPopup != null)
                _currentPopup.Hide();

            base.OnDisplayEnd();
        }

        protected override ITimer CreateDisplayTimer() => new CoroutineTimer(this);

        protected override ITimer CreateSafetyTimer() => new CoroutineTimer(this);
    }
}
