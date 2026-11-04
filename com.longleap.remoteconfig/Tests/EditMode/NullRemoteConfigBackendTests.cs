using NUnit.Framework;

namespace LongLeap.RemoteConfig.Tests
{
    [TestFixture]
    public class NullRemoteConfigBackendTests
    {
        [Test]
        public void FetchAsync_CompletesSynchronouslyWithEmptyFailure()
        {
            var task = new NullRemoteConfigBackend().FetchAsync();

            Assert.IsTrue(task.IsCompleted, "the null backend must complete without an async delay");
            Assert.IsFalse(task.Result.IsSuccess);
            Assert.IsNull(task.Result.Parameters);
            Assert.IsNull(task.Result.Experiments);
        }
    }
}
