using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace LongLeap.UI.Tests
{
    /// <summary>
    /// PlayMode tests for the parts of <see cref="WindowService"/> that do not load prefabs:
    /// catalog validation, unknown windows, the home window and EventSystem ownership.
    /// Creating windows needs Addressables and is not covered here.
    /// </summary>
    [TestFixture]
    public sealed class WindowServiceTests
    {
        private sealed class UnknownWindow : WindowBase
        {
        }

        private GameObject _serviceObject;
        private GameObject _foreignObject;

        [TearDown]
        public void TearDown()
        {
            // Immediate: a leftover EventSystem would make the next test's service warn about it.
            if (_serviceObject != null)
                Object.DestroyImmediate(_serviceObject);

            if (_foreignObject != null)
                Object.DestroyImmediate(_foreignObject);
        }

        private WindowService CreateService(WindowEntry[] catalog = null)
        {
            // Inactive first, so the catalog is in place before Awake indexes it.
            _serviceObject = new GameObject("WindowServiceTests");
            _serviceObject.SetActive(false);

            var service = _serviceObject.AddComponent<WindowService>();
            typeof(WindowService)
                .GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(service, catalog ?? Array.Empty<WindowEntry>());

            _serviceObject.SetActive(true);
            return service;
        }

        private static void AssertFaulted<TException>(Task task) where TException : Exception
        {
            Assert.IsTrue(task.IsFaulted, "The task must fail synchronously.");
            Assert.IsInstanceOf<TException>(task.Exception?.InnerException);
        }

        [Test]
        public void UnknownWindow_IsNotRegisteredOrOpen()
        {
            var service = CreateService();

            Assert.IsFalse(service.IsRegistered<UnknownWindow>());
            Assert.IsFalse(service.TryGet<UnknownWindow>(out var window));
            Assert.IsNull(window);
            Assert.IsFalse(service.IsOpen<UnknownWindow>());
        }

        [Test]
        public void UnknownWindow_OpenLoadUnload_Fail()
        {
            var service = CreateService();

            AssertFaulted<InvalidOperationException>(service.OpenAsync<UnknownWindow>());
            AssertFaulted<InvalidOperationException>(service.LoadAsync<UnknownWindow>());
            AssertFaulted<InvalidOperationException>(service.UnloadAsync<UnknownWindow>());
            Assert.IsFalse(service.IsOpen<UnknownWindow>(), "A failed open must not leave the window marked open.");
        }

        [Test]
        public void UnknownWindow_CloseIsNoOp()
        {
            var service = CreateService();

            Assert.IsTrue(service.CloseAsync<UnknownWindow>().IsCompletedSuccessfully);
            Assert.IsTrue(service.CloseAsync(null).IsCompletedSuccessfully);
            Assert.IsTrue(service.CloseAllAsync().IsCompletedSuccessfully);
        }

        [Test]
        public void UnknownWindow_OnCreated_LogsErrorAndDoesNotThrow()
        {
            var service = CreateService();

            LogAssert.Expect(LogType.Error, new Regex(@"^\[Windows\] UnknownWindow"));
            service.OnCreated<UnknownWindow>(_ => Assert.Fail("Setup must not run."));
        }

        [Test]
        public void OpenHome_WithoutHome_WarnsAndCompletes()
        {
            var service = CreateService();

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Windows\].*SetHome"));
            Assert.IsTrue(service.OpenHomeAsync().IsCompletedSuccessfully);
        }

        [Test]
        public void OpenHome_UnknownHomeWindow_Fails()
        {
            var service = CreateService();
            service.SetHome<UnknownWindow>();

            AssertFaulted<InvalidOperationException>(service.OpenHomeAsync());
        }

        [Test]
        public void CatalogRowWithoutPrefab_IsSkippedWithError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[Windows\].*\b0\b"));

            var service = CreateService(new[] { new WindowEntry() });

            Assert.IsTrue(service.InitializeAsync().IsCompletedSuccessfully, "Nothing to preload.");
        }

        [Test]
        public void Awake_CreatesOwnEventSystemAndClickBinder()
        {
            var service = CreateService();

            var eventSystem = service.GetComponentInChildren<EventSystem>();
            Assert.IsNotNull(eventSystem);
            Assert.IsNotNull(eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>());
            Assert.AreSame(eventSystem, EventSystem.current);
            Assert.IsNotNull(service.GetComponent<MenuClickAudioBinder>());
        }

        [Test]
        public void Awake_DisablesForeignEventSystem()
        {
            _foreignObject = new GameObject("ForeignEventSystem");
            var foreign = _foreignObject.AddComponent<EventSystem>();

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Windows\].*ForeignEventSystem"));
            var service = CreateService();

            Assert.IsFalse(foreign.enabled);
            Assert.AreSame(service.GetComponentInChildren<EventSystem>(), EventSystem.current);
        }
    }
}
