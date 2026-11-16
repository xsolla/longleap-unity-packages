using System.Threading.Tasks;
using LongLeap.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LongLeap.RemoteConfig.Tests
{
    [TestFixture]
    public sealed class NullRemoteConfigProviderTests
    {
        [Test]
        public void FetchAsync_CompletesSynchronouslyWithEmptyFailure()
        {
            var task = new NullRemoteConfigProvider().FetchAsync();

            Assert.IsTrue(task.IsCompleted, "the null provider must complete without an async delay");
            Assert.IsFalse(task.Result.IsSuccess);
            Assert.AreEqual(0, task.Result.Parameters.Count);
            Assert.AreEqual(0, task.Result.Experiments.Count);
        }

        [Test]
        public async Task WithService_FetchFailsQuietlyAndDefaultsAreUsed()
        {
            var service = new RemoteConfigService(new NullRemoteConfigProvider(), new InMemoryPlayerPrefs());

            Assert.AreEqual(RemoteConfigFetchStatus.Failure, await service.FetchAsync());
            Assert.AreEqual(1.5f, service.GetFloat("any", 1.5f));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
