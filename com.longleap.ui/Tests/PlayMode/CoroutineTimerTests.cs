using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace LongLeap.Notifications.Tests
{
    [TestFixture]
    public sealed class CoroutineTimerTests
    {
        private const float WaitSeconds = 0.2f;

        private GameObject _go;
        private TimerHost _host;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("CoroutineTimerTests");
            _host = _go.AddComponent<TimerHost>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        [UnityTest]
        public IEnumerator Start_FiresOnceAfterDuration()
        {
            var timer = new CoroutineTimer(_host);
            var fired = 0;

            timer.Start(TimeSpan.FromSeconds(0.05), () => fired++);

            Assert.AreEqual(0, fired, "Must not fire synchronously.");
            yield return new WaitForSecondsRealtime(WaitSeconds);
            Assert.AreEqual(1, fired);
        }

        [UnityTest]
        public IEnumerator Start_IgnoresTimeScale()
        {
            var previous = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                var timer = new CoroutineTimer(_host);
                var fired = false;

                timer.Start(TimeSpan.FromSeconds(0.05), () => fired = true);
                yield return new WaitForSecondsRealtime(WaitSeconds);

                Assert.IsTrue(fired);
            }
            finally
            {
                Time.timeScale = previous;
            }
        }

        [UnityTest]
        public IEnumerator Cancel_PreventsCallback()
        {
            var timer = new CoroutineTimer(_host);
            var fired = false;

            timer.Start(TimeSpan.FromSeconds(0.05), () => fired = true);
            timer.Cancel();
            yield return new WaitForSecondsRealtime(WaitSeconds);

            Assert.IsFalse(fired);
        }

        [UnityTest]
        public IEnumerator Start_WhileRunning_ReplacesCountdown()
        {
            var timer = new CoroutineTimer(_host);
            var first = false;
            var second = false;

            timer.Start(TimeSpan.FromSeconds(0.05), () => first = true);
            timer.Start(TimeSpan.FromSeconds(0.05), () => second = true);
            yield return new WaitForSecondsRealtime(WaitSeconds);

            Assert.IsFalse(first);
            Assert.IsTrue(second);
        }

        [UnityTest]
        public IEnumerator Start_FromCallback_RestartsTimer()
        {
            var timer = new CoroutineTimer(_host);
            var fired = 0;

            timer.Start(TimeSpan.Zero, () =>
            {
                fired++;
                if (fired == 1)
                    timer.Start(TimeSpan.Zero, () => fired++);
            });
            yield return new WaitForSecondsRealtime(WaitSeconds);

            Assert.AreEqual(2, fired);
        }

        [UnityTest]
        public IEnumerator Start_NegativeDuration_FiresLikeZero()
        {
            var timer = new CoroutineTimer(_host);
            var fired = false;

            timer.Start(TimeSpan.FromSeconds(-5), () => fired = true);
            yield return new WaitForSecondsRealtime(WaitSeconds);

            Assert.IsTrue(fired);
        }

        [Test]
        public void NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new CoroutineTimer(null), "Null host.");
            Assert.Throws<ArgumentNullException>(
                () => new CoroutineTimer(_host).Start(TimeSpan.Zero, null), "Null callback.");
        }

        [Test]
        public void Start_InactiveHost_Throws()
        {
            var timer = new CoroutineTimer(_host);
            _go.SetActive(false);

            Assert.Throws<InvalidOperationException>(() => timer.Start(TimeSpan.Zero, () => { }));
        }

        [Test]
        public void Start_DestroyedHost_Throws()
        {
            var timer = new CoroutineTimer(_host);
            Object.DestroyImmediate(_go);

            Assert.Throws<InvalidOperationException>(() => timer.Start(TimeSpan.Zero, () => { }));
        }

        [Test]
        public void Cancel_WithoutStart_DoesNothing()
        {
            var timer = new CoroutineTimer(_host);

            Assert.DoesNotThrow(timer.Cancel);
        }
    }
}
