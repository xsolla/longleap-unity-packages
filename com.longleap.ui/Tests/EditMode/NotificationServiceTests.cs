using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        public void ShowWhileBusy_QueuesUntilComplete_ThenFiresNext()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("Second"));

            Assert.AreEqual(1, invoked.Count, "Second must wait while First is on screen.");
            Assert.AreEqual("First", invoked[0].data.Title);

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
        public void ShowWithNoSubscribers_QueuesUntilSubscribed()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();

            service.Show(new NotificationData("Low", "", null, NotificationPriority.Low));
            service.Show(new NotificationData("High", "", null, NotificationPriority.High));

            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            Assert.AreEqual(1, invoked.Count);
            Assert.AreEqual("High", invoked[0].data.Title);

            invoked[0].onComplete();
            Assert.AreEqual(2, invoked.Count);
            Assert.AreEqual("Low", invoked[1].data.Title);
        }

        [Test]
        public void SubscriberRemovedDuringShow_QueueKeptUntilResubscribed()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            Action<NotificationData, Action> handler = (data, onComplete) => invoked.Add((data, onComplete));
            service.OnShowRequest += handler;

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("Second"));
            service.Show(new NotificationData("Third"));

            service.OnShowRequest -= handler;
            invoked[0].onComplete();
            Assert.AreEqual(1, invoked.Count, "Nothing should be delivered while no view is subscribed.");

            service.OnShowRequest += handler;
            Assert.AreEqual(2, invoked.Count);
            Assert.AreEqual("Second", invoked[1].data.Title);

            invoked[1].onComplete();
            Assert.AreEqual("Third", invoked[2].data.Title);
        }

        [Test]
        public void SubscribingWhileBusy_DoesNotStartSecondShow()
        {
            var service = new NotificationService();
            var first = new List<string>();
            var second = new List<string>();
            service.OnShowRequest += (data, onComplete) => first.Add(data.Title);

            service.Show(new NotificationData("Active"));
            service.Show(new NotificationData("Queued"));
            service.OnShowRequest += (data, onComplete) => second.Add(data.Title);

            CollectionAssert.AreEqual(new[] { "Active" }, first);
            Assert.IsEmpty(second);
        }

        [Test]
        public void Queue_OrdersByPriorityThenFifo_WithoutPreemptingActive()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            NotificationData Make(string title, NotificationPriority priority) =>
                new NotificationData(title, "", TimeSpan.FromSeconds(3), priority);

            service.Show(Make("Active", NotificationPriority.Low));
            service.Show(Make("Low", NotificationPriority.Low));
            service.Show(Make("Default1", NotificationPriority.Default));
            service.Show(Make("Critical", NotificationPriority.Critical));
            service.Show(Make("Default2", NotificationPriority.Default));

            Assert.AreEqual(1, invoked.Count, "A queued Critical must not interrupt the active notification.");

            for (var i = 0; i < 4; i++)
                invoked[i].onComplete();

            CollectionAssert.AreEqual(
                new[] { "Active", "Critical", "Default1", "Default2", "Low" },
                invoked.ConvertAll(call => call.data.Title));
        }

        [Test]
        public void DuplicateComplete_DoesNotAdvanceQueueTwice()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("Second"));
            service.Show(new NotificationData("Third"));

            invoked[0].onComplete();
            LogAssert.Expect(LogType.Warning, "NotificationService: ignored a duplicate or stale completion callback.");
            invoked[0].onComplete();

            Assert.AreEqual(2, invoked.Count);
            Assert.AreEqual("Second", invoked[1].data.Title);
        }

        [Test]
        public void StaleComplete_WhileNextIsShowing_IsIgnored()
        {
            var service = new NotificationService();
            var invoked = new List<(NotificationData data, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) => invoked.Add((data, onComplete));

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("Second"));
            invoked[0].onComplete();

            LogAssert.Expect(LogType.Warning, "NotificationService: ignored a duplicate or stale completion callback.");
            invoked[0].onComplete();

            service.Show(new NotificationData("Third"));
            Assert.AreEqual(2, invoked.Count, "Second must still be considered active, so Third stays queued.");
        }

        [Test]
        public void HandlerThrows_ServiceRecoversForNextShow()
        {
            var service = new NotificationService();
            var shown = new List<string>();
            service.OnShowRequest += (data, onComplete) =>
            {
                if (data.Title == "Bad")
                    throw new InvalidOperationException("boom");

                shown.Add(data.Title);
            };

            LogAssert.Expect(LogType.Error, new Regex("OnShowRequest handler threw"));
            service.Show(new NotificationData("Bad"));
            service.Show(new NotificationData("Good"));

            CollectionAssert.AreEqual(new[] { "Good" }, shown);
        }

        [Test]
        public void HandlerThrows_WithQueuedItems_ShowsNextQueued()
        {
            var service = new NotificationService();
            var shown = new List<(string title, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) =>
            {
                if (data.Title == "Bad")
                    throw new InvalidOperationException("boom");

                shown.Add((data.Title, onComplete));
            };

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("Bad"));
            service.Show(new NotificationData("Good"));

            LogAssert.Expect(LogType.Error, new Regex("OnShowRequest handler threw"));
            shown[0].onComplete();

            Assert.AreEqual(2, shown.Count);
            Assert.AreEqual("Good", shown[1].title);
        }

        [Test]
        public void HandlerCompletesThenThrows_DoesNotAdvanceTwice()
        {
            var service = new NotificationService();
            var shown = new List<(string title, Action onComplete)>();
            service.OnShowRequest += (data, onComplete) =>
            {
                shown.Add((data.Title, onComplete));

                if (data.Title == "A")
                {
                    onComplete();
                    throw new InvalidOperationException("boom");
                }
            };

            service.Show(new NotificationData("First"));
            service.Show(new NotificationData("A"));
            service.Show(new NotificationData("B"));

            LogAssert.Expect(LogType.Error, new Regex("OnShowRequest handler threw"));
            shown[0].onComplete();

            service.Show(new NotificationData("C"));

            CollectionAssert.AreEqual(new[] { "First", "A", "B" }, shown.ConvertAll(s => s.title));
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
