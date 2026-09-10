using System;
using System.Collections.Generic;
using NUnit.Framework;
using LongLeap.Notifications;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Notifications.Tests
{
    [TestFixture]
    public class NotificationServiceTests
    {
        [Test]
        public void ShowWhenIdle_FiresEventOnce()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            var data = new NotificationData("Test", "Hello", TimeSpan.FromSeconds(3), NotificationPriority.Default);
            service.Show(data);

            Assert.AreEqual(1, invoked.Count);
            Assert.AreEqual("Test", invoked[0].data.Title);
            Assert.AreEqual("Hello", invoked[0].data.Message);
            Assert.IsNotNull(invoked[0].onComplete);
        }

        [Test]
        public void ShowWhileBusy_EnqueuesData()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("Second"));

            Assert.AreEqual(1, invoked.Count);
            Assert.AreEqual("First", invoked[0].data.Title);
        }

        [Test]
        public void OnViewComplete_WithQueuedItem_FiresNextEvent()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("Second"));

            invoked[0].onComplete();

            Assert.AreEqual(2, invoked.Count);
            Assert.AreEqual("Second", invoked[1].data.Title);
        }

        [Test]
        public void OnViewComplete_EmptyQueue_SetsIdle()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("Only"));
            invoked[0].onComplete();

            Assert.AreEqual(1, invoked.Count);
            service.Show(new NotificationData("Second"));
            Assert.AreEqual(2, invoked.Count);
            Assert.AreEqual("Second", invoked[1].data.Title);
        }

        [Test]
        public void ClearPending_WhileShowing_AffectsQueueOnly()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("Active"));
            service.Show(new NotificationData("Queued"));
            service.ClearPending();
            invoked[0].onComplete();

            Assert.AreEqual(1, invoked.Count);
        }

        [Test]
        public void ShowWithNoSubscribers_LogsError()
        {
            var service = new NotificationService();

            LogAssert.Expect(LogType.Error, "Cannot show notification: no subscribers registered for OnShowRequest.");
            service.Show(new NotificationData("Orphan"));
        }

        [Test]
        public void HighPriority_AfterLowPriority_WhileBusy_ShowsHighFirst()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("Low1", "", TimeSpan.FromSeconds(3), NotificationPriority.Low));
            service.Show(new NotificationData("Low2", "", TimeSpan.FromSeconds(3), NotificationPriority.Low));
            service.Show(new NotificationData("High", "", TimeSpan.FromSeconds(3), NotificationPriority.Critical));

            invoked[0].onComplete();

            Assert.AreEqual(2, invoked.Count);
            Assert.AreEqual("High", invoked[1].data.Title);
        }

        [Test]
        public void SamePriority_DequeuesFifo()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("Second"));
            service.Show(new NotificationData("Third"));

            invoked[0].onComplete();
            Assert.AreEqual("Second", invoked[1].data.Title);

            invoked[1].onComplete();
            Assert.AreEqual("Third", invoked[2].data.Title);
        }

        [Test]
        public void QueueExceedingCapacity_LogsWarning()
        {
            var service = new NotificationService();
            service.OnShowRequest += (data, onComplete) => { };

            service.Show(new NotificationData("Active"));
            for (int i = 0; i < 10; i++)
            {
                service.Show(new NotificationData($"Queued{i}"));
            }

            LogAssert.Expect(LogType.Warning, "Notification queue has 11 items, exceeding the recommended limit of 10.");
            service.Show(new NotificationData("Overflow"));
        }
    }
}
