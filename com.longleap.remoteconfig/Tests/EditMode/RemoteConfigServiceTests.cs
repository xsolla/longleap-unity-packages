using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.RemoteConfig.Tests
{
    // Fetching, deduplication, cancellation, throttling, activation and disposal.
    [TestFixture]
    public sealed class RemoteConfigServiceTests
    {
        private FakeRemoteConfigProvider _provider;
        private InMemoryPlayerPrefs _prefs;
        private DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _provider = new FakeRemoteConfigProvider();
            _prefs = new InMemoryPlayerPrefs();
            _now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        }

        private RemoteConfigService Create(Action<RemoteConfigOptions> configure = null)
        {
            var options = new RemoteConfigOptions { UtcNow = () => _now };
            configure?.Invoke(options);
            return new RemoteConfigService(_provider, _prefs, options);
        }

        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new RemoteConfigService(null, _prefs));
            Assert.Throws<ArgumentNullException>(() => new RemoteConfigService(_provider, null));
            Assert.Throws<ArgumentException>(() => Create(o => o.CacheKey = ""));
            Assert.Throws<ArgumentException>(() => Create(o => o.MinimumFetchInterval = TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentException>(() => Create(o => o.UtcNow = null));
        }

        [Test]
        public async Task Fetch_Success_AppliesValuesAndExperiments()
        {
            var service = Create();
            _provider.Result = FakeRemoteConfigProvider.Success(
                new[] { ("str_key", "hello"), ("int_key", "42") },
                new[] { new AbExperimentInfo("exp_01", 1), new AbExperimentInfo("exp_02", 2) });

            var status = await service.FetchAsync();

            Assert.AreEqual(RemoteConfigFetchStatus.Success, status);
            Assert.AreEqual(RemoteConfigFetchStatus.Success, service.LastFetchStatus);
            Assert.AreEqual(_now, service.LastSuccessfulFetchUtc);
            Assert.AreEqual("hello", service.GetString("str_key", "def"));
            Assert.AreEqual(42, service.GetInt("int_key", 0));
            Assert.AreEqual(2, service.ActiveExperiments.Count);
            Assert.AreEqual("exp_02", service.ActiveExperiments[1].ExperimentId);
            Assert.AreEqual(2, service.ActiveExperiments[1].VariantIndex);
        }

        [Test]
        public async Task Fetch_SuccessReplacesTheWholeSet()
        {
            var service = Create();
            _provider.Result = FakeRemoteConfigProvider.Success(("old", "1"));
            await service.FetchAsync();

            _provider.Result = RemoteConfigFetchResult.Success(null);
            await service.FetchAsync();

            Assert.IsFalse(service.HasKey("old"));
            Assert.AreEqual(0, service.Values.Count);
        }

        [Test]
        public async Task Fetch_Failure_KeepsPreviousValues()
        {
            var service = Create();
            _provider.Result = FakeRemoteConfigProvider.Success(("key", "first"));
            await service.FetchAsync();

            _provider.Result = RemoteConfigFetchResult.Failure;
            var status = await service.FetchAsync();

            Assert.AreEqual(RemoteConfigFetchStatus.Failure, status);
            Assert.AreEqual(RemoteConfigFetchStatus.Failure, service.LastFetchStatus);
            Assert.AreEqual("first", service.GetString("key", "def"));
        }

        [Test]
        public async Task Fetch_ProviderThrowsOrReturnsNull_ReturnsFailure()
        {
            var service = Create();

            _provider.Throw = new InvalidOperationException("boom");
            LogAssert.Expect(LogType.Warning, new Regex("RemoteConfigService: the provider threw: boom"));
            Assert.AreEqual(RemoteConfigFetchStatus.Failure, await service.FetchAsync());

            _provider.Throw = null;
            _provider.Result = null;
            Assert.AreEqual(RemoteConfigFetchStatus.Failure, await service.FetchAsync());
        }

        [Test]
        public async Task Fetch_ConcurrentCalls_ShareOneRequest()
        {
            var service = Create();
            _provider.Pending = new TaskCompletionSource<RemoteConfigFetchResult>();

            var first = service.FetchAsync();
            var second = service.FetchAsync();
            var third = service.FetchAsync();
            _provider.Pending.SetResult(FakeRemoteConfigProvider.Success(("key", "val")));
            var statuses = await Task.WhenAll(first, second, third);

            Assert.AreEqual(1, _provider.CallCount, "the provider must be called once");
            CollectionAssert.AreEqual(new[] { RemoteConfigFetchStatus.Success, RemoteConfigFetchStatus.Success, RemoteConfigFetchStatus.Success }, statuses);
            Assert.AreEqual("val", service.GetString("key", "default"));
        }

        [Test]
        public async Task Fetch_AfterCompletion_StartsANewRequest()
        {
            var service = Create();

            await service.FetchAsync();
            await service.FetchAsync();

            Assert.AreEqual(2, _provider.CallCount);
        }

        [Test]
        public async Task Fetch_CallerCancels_ReturnsCanceledButResultIsStillApplied()
        {
            var service = Create();
            _provider.Pending = new TaskCompletionSource<RemoteConfigFetchResult>();
            var cts = new CancellationTokenSource();

            var waiting = service.FetchAsync(cts.Token);
            cts.Cancel();
            Assert.AreEqual(RemoteConfigFetchStatus.Canceled, await waiting);

            _provider.Pending.SetResult(FakeRemoteConfigProvider.Success(("key", "late")));
            // Joins the request still in flight (or repeats it with the same result).
            Assert.AreEqual(RemoteConfigFetchStatus.Success, await service.FetchAsync());

            Assert.AreEqual("late", service.GetString("key", "default"));
        }

        [Test]
        public async Task Fetch_TokenAlreadyCanceled_DoesNotCallProvider()
        {
            var service = Create();

            var status = await service.FetchAsync(new CancellationToken(true));

            Assert.AreEqual(RemoteConfigFetchStatus.Canceled, status);
            Assert.AreEqual(0, _provider.CallCount);
        }

        [Test]
        public async Task Fetch_MinimumInterval_ThrottlesUntilElapsed_AlsoAcrossLaunches()
        {
            void Configure(RemoteConfigOptions o) => o.MinimumFetchInterval = TimeSpan.FromMinutes(10);
            var service = Create(Configure);
            _provider.Result = FakeRemoteConfigProvider.Success(("key", "val"));
            await service.FetchAsync();

            _now = _now.AddMinutes(5);
            Assert.AreEqual(RemoteConfigFetchStatus.Throttled, await service.FetchAsync());
            Assert.AreEqual(RemoteConfigFetchStatus.Throttled, await Create(Configure).FetchAsync(), "the fetch time is cached");
            Assert.AreEqual(1, _provider.CallCount);

            _now = _now.AddMinutes(5);
            Assert.AreEqual(RemoteConfigFetchStatus.Success, await service.FetchAsync());
            Assert.AreEqual(2, _provider.CallCount);
        }

        [Test]
        public async Task Fetch_DeferredActivation_AppliesOnActivateOrNextLaunch()
        {
            var service = Create(o => o.ActivateOnFetch = false);
            var updates = 0;
            service.Updated += () => updates++;
            _provider.Result = FakeRemoteConfigProvider.Success(
                new[] { ("key", "new") }, new[] { new AbExperimentInfo("exp", 1) });

            Assert.AreEqual(RemoteConfigFetchStatus.Success, await service.FetchAsync());
            Assert.IsTrue(service.HasPendingValues);
            Assert.IsFalse(service.HasKey("key"));
            Assert.AreEqual(0, service.ActiveExperiments.Count);
            Assert.AreEqual(0, updates);

            Assert.AreEqual("new", Create().GetString("key", "def"), "the next launch reads the fetched values");

            Assert.IsTrue(service.Activate());
            Assert.AreEqual("new", service.GetString("key", "def"));
            Assert.AreEqual(1, service.ActiveExperiments.Count);
            Assert.AreEqual(1, updates);
            Assert.IsFalse(service.Activate(), "nothing left to activate");
        }

        [Test]
        public async Task Updated_RaisedOnApply_AndAFailingHandlerDoesNotStopOthers()
        {
            var service = Create();
            var calls = 0;
            service.Updated += () => throw new InvalidOperationException("handler failed");
            service.Updated += () => calls++;
            _provider.Result = FakeRemoteConfigProvider.Success(("key", "val"));

            LogAssert.Expect(LogType.Exception, new Regex("handler failed"));
            var status = await service.FetchAsync();

            Assert.AreEqual(RemoteConfigFetchStatus.Success, status);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public async Task Dispose_DuringFetch_CancelsProviderAndDropsResult()
        {
            var service = Create();
            _provider.Pending = new TaskCompletionSource<RemoteConfigFetchResult>();

            var fetch = service.FetchAsync();
            service.Dispose();
            Assert.IsTrue(_provider.LastToken.IsCancellationRequested, "the provider's token is cancelled");

            _provider.Pending.SetResult(FakeRemoteConfigProvider.Success(("key", "late")));

            Assert.AreEqual(RemoteConfigFetchStatus.Canceled, await fetch);
            Assert.IsFalse(service.HasKey("key"));
            Assert.AreEqual(RemoteConfigFetchStatus.Canceled, await service.FetchAsync());
        }

        [Test]
        public async Task Dispose_ClearsStateAndGettersStaySilent()
        {
            var service = Create();
            _provider.Result = FakeRemoteConfigProvider.Success(
                new[] { ("key", "val") }, new[] { new AbExperimentInfo("exp", 1) });
            await service.FetchAsync();

            service.Dispose();

            Assert.AreEqual("default", service.GetString("key", "default"));
            Assert.AreEqual(10, service.GetInt("key", 10));
            Assert.AreEqual(1.0f, service.GetFloat("key", 1.0f));
            Assert.AreEqual(true, service.GetBool("key", true));
            Assert.AreEqual(0, service.ActiveExperiments.Count);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
