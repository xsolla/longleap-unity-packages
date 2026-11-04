using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LongLeap.Localization.Tests
{
    public class LocTests
    {
        private sealed class StubService : ILocalizationService
        {
            public readonly Dictionary<string, string> Strings = new Dictionary<string, string>();

            public event Action OnLanguageChanged;
            public bool IsReady { get; set; } = true;

            public string Resolve(string key)
                => string.IsNullOrEmpty(key) ? string.Empty
                    : Strings.TryGetValue(key, out var value) ? value : key;

            public string ResolveFormat(string key, params object[] args)
                => string.Format(Resolve(key), args);

            public void RaiseLanguageChanged() => OnLanguageChanged?.Invoke();
            public bool HasSubscribers => OnLanguageChanged != null;
        }

        private int _notifications;

        private void CountNotification() => _notifications++;

        // Loc is static and may still hold a service from a Play session run with
        // domain reload disabled. Attaching and resetting a stub detaches it.
        private static void DetachService()
        {
            var stub = new StubService();
            Loc.Init(stub);
            Loc.Reset(stub);
        }

        [SetUp]
        public void SetUp()
        {
            DetachService();
            _notifications = 0;
            Loc.OnLanguageChanged += CountNotification;
        }

        [TearDown]
        public void TearDown()
        {
            Loc.OnLanguageChanged -= CountNotification;
            DetachService();
        }

        [Test]
        public void NoService_ResolveReturnsKey_AndEmptyForNull()
        {
            Assert.IsFalse(Loc.IsReady);
            Assert.AreEqual("my_key", Loc.Resolve("my_key"));
            Assert.AreEqual(string.Empty, Loc.Resolve(null));
        }

        [Test]
        public void NoService_ResolveFormat_FormatsKey()
        {
            Assert.AreEqual("format_42", Loc.ResolveFormat("format_{0}", 42));
        }

        [Test]
        public void NoService_ResolveFormat_MalformedKey_ReturnsKey()
        {
            Assert.AreEqual("bad{key", Loc.ResolveFormat("bad{key", 1));
        }

        [Test]
        public void Init_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Loc.Init(null));
        }

        [Test]
        public void Init_NotifiesAndDelegatesToService()
        {
            var service = new StubService();
            service.Strings["hello"] = "Hello";

            Loc.Init(service);

            Assert.AreEqual(1, _notifications);
            Assert.IsTrue(Loc.IsReady);
            Assert.AreEqual("Hello", Loc.Resolve("hello"));

            service.IsReady = false;
            Assert.IsFalse(Loc.IsReady);
        }

        [Test]
        public void Init_ForwardsServiceEvents_AndUnsubscribesReplacedService()
        {
            var first = new StubService();
            var second = new StubService();

            Loc.Init(first);
            Loc.Init(second);
            _notifications = 0;

            Assert.IsFalse(first.HasSubscribers);

            first.RaiseLanguageChanged();
            Assert.AreEqual(0, _notifications);

            second.RaiseLanguageChanged();
            Assert.AreEqual(1, _notifications);
        }

        [Test]
        public void Reset_DetachesOnlyTheAttachedService()
        {
            var attached = new StubService();
            var other = new StubService();
            attached.Strings["hello"] = "Hello";

            Loc.Init(attached);

            Loc.Reset(other);
            Assert.AreEqual("Hello", Loc.Resolve("hello"));

            Loc.Reset(attached);
            Assert.AreEqual("hello", Loc.Resolve("hello"));
            Assert.IsFalse(attached.HasSubscribers);
        }
    }
}
