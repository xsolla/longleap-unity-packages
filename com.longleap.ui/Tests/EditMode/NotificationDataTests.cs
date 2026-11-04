using System;
using NUnit.Framework;
using LongLeap.Notifications;

namespace LongLeap.Notifications.Tests
{
    [TestFixture]
    public class NotificationDataTests
    {
        [Test]
        public void FullConstructor_SetsAllProperties()
        {
            var data = new NotificationData("Test Title", "Test Message", TimeSpan.FromSeconds(5), NotificationPriority.Critical);

            Assert.AreEqual("Test Title", data.Title);
            Assert.AreEqual("Test Message", data.Message);
            Assert.AreEqual(TimeSpan.FromSeconds(5), data.Duration);
            Assert.AreEqual(NotificationPriority.Critical, data.Priority);
        }

        [Test]
        public void TitleOnlyConstructor_SetsDefaultValues()
        {
            var data = new NotificationData("Title Only");

            Assert.AreEqual("Title Only", data.Title);
            Assert.AreEqual("", data.Message);
            Assert.AreEqual(TimeSpan.FromSeconds(3), data.Duration);
            Assert.AreEqual(NotificationPriority.Default, data.Priority);
        }
    }
}
