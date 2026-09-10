using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Notifications.Tests
{
    [TestFixture]
    public class NotificationSystemViewTests
    {
        private sealed class FakeTimer : ITimer
        {
            public Action OnElapsed { get; private set; }
            public TimeSpan LastDuration { get; private set; }
            public bool WasCancelled { get; private set; }

            public void Start(TimeSpan duration, Action onElapsed)
            {
                Cancel();
                WasCancelled = false;
                LastDuration = duration;
                OnElapsed = onElapsed;
            }

            public void Cancel()
            {
                WasCancelled = true;
                OnElapsed = null;
            }

            public void Fire()
            {
                var callback = OnElapsed;
                OnElapsed = null;
                callback?.Invoke();
            }
        }

        private sealed class TestNotificationView : NotificationSystemView
        {
            public FakeTimer FakeDisplayTimer { get; } = new FakeTimer();
            public FakeTimer FakeSafetyTimer { get; } = new FakeTimer();

            public NotificationData ReceivedData { get; private set; }
            public int OnShowCallCount { get; private set; }
            public int OnDisplayEndCallCount { get; private set; }
            public int OnDestroyCallCount { get; private set; }
            public GameObject SpawnedPopup { get; private set; }

            protected override ITimer CreateDisplayTimer() => FakeDisplayTimer;

            protected override ITimer CreateSafetyTimer() => FakeSafetyTimer;

            protected override void OnShow(NotificationData data)
            {
                ReceivedData = data;
                OnShowCallCount++;
                var go = new GameObject("TestPopup");
                SpawnedPopup = go;
                ActivePopup = go;
            }

            protected override void OnDisplayEnd()
            {
                OnDisplayEndCallCount++;
                base.OnDisplayEnd();
            }

            protected override TimeSpan SafetyTimeout => TimeSpan.FromSeconds(0.05);

            protected override void DestroyPopup(GameObject popup)
            {
                DestroyImmediate(popup);
            }

            public void InvokeComplete() => Complete();

            public void InvokeOnDestroy() => OnDestroy();

            protected override void OnDestroy()
            {
                OnDestroyCallCount++;
                base.OnDestroy();
            }
        }

        private GameObject _go;
        private TestNotificationView _view;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestView");
            _view = _go.AddComponent<TestNotificationView>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void OnShowRequest_CallsOnShow()
        {
            var data = new NotificationData("Test", "Message", TimeSpan.FromSeconds(3), NotificationPriority.Default);
            Action callback = () => { };

            _view.OnShowRequest(data, callback);

            Assert.AreEqual(1, _view.OnShowCallCount);
            Assert.AreEqual("Test", _view.ReceivedData.Title);
            Assert.AreEqual("Message", _view.ReceivedData.Message);
            Assert.AreEqual(TimeSpan.FromSeconds(3), _view.ReceivedData.Duration);
            Assert.AreEqual(NotificationPriority.Default, _view.ReceivedData.Priority);
        }

        [Test]
        public void DurationZero_CallsOnDisplayEndSynchronously()
        {
            var data = new NotificationData("Test", "", TimeSpan.Zero);

            _view.OnShowRequest(data, () => { });

            Assert.AreEqual(1, _view.OnDisplayEndCallCount);
        }

        [Test]
        public void DurationNegative_SameAsZero()
        {
            var data = new NotificationData("Test", "", TimeSpan.FromSeconds(-1));

            _view.OnShowRequest(data, () => { });

            Assert.AreEqual(1, _view.OnDisplayEndCallCount);
        }

        [Test]
        public void CompleteCalledBeforeSafety_CallbackFiresNoForceDestroy()
        {
            var data = new NotificationData("Test", "", TimeSpan.FromSeconds(1));
            var callbackInvoked = false;

            _view.OnShowRequest(data, () => callbackInvoked = true);
            _view.InvokeComplete();

            Assert.IsTrue(callbackInvoked);
            Assert.IsNotNull(_view.SpawnedPopup);
        }

        [Test]
        public void OnShowRequestWhileBusy_LogsError()
        {
            var data = new NotificationData("Test", "", TimeSpan.FromSeconds(1));

            _view.OnShowRequest(data, () => { });

            LogAssert.Expect(LogType.Error, "NotificationSystemView: OnShowRequest called while a notification is already active.");
            _view.OnShowRequest(data, () => { });
        }

        [Test]
        public void OnDestroy_CallsComplete()
        {
            var data = new NotificationData("Test", "", TimeSpan.FromSeconds(1));
            var callbackInvoked = false;

            _view.OnShowRequest(data, () => callbackInvoked = true);
            _view.InvokeOnDestroy();

            Assert.AreEqual(1, _view.OnDestroyCallCount);
            Assert.IsTrue(callbackInvoked);
        }

        [Test]
        public void Complete_IsIdempotent()
        {
            var data = new NotificationData("Test", "", TimeSpan.FromSeconds(1));
            var invokeCount = 0;

            _view.OnShowRequest(data, () => invokeCount++);
            _view.InvokeComplete();
            _view.InvokeComplete();

            Assert.AreEqual(1, invokeCount);
        }

        [Test]
        public void Complete_ThenNewShowRequest_InvokesSecondCallback()
        {
            var firstInvoked = false;
            var secondInvoked = false;

            _view.OnShowRequest(new NotificationData("First"), () => firstInvoked = true);
            _view.InvokeComplete();

            _view.OnShowRequest(new NotificationData("Second"), () => secondInvoked = true);
            _view.InvokeComplete();

            Assert.IsTrue(firstInvoked, "First callback should have been invoked.");
            Assert.IsTrue(secondInvoked, "Second callback should have been invoked.");
        }

        [Test]
        public void DerivedNeverCallsComplete_SafetyTimerFires_CallbackInvoked_PopupDestroyed()
        {
            var callbackInvoked = false;
            var data = new NotificationData("Test", "", TimeSpan.FromMilliseconds(10));

            _view.OnShowRequest(data, () => callbackInvoked = true);

            _view.FakeDisplayTimer.Fire();
            _view.FakeSafetyTimer.Fire();

            Assert.IsTrue(callbackInvoked);
            Assert.IsTrue(_view.SpawnedPopup == null);
        }
    }
}
