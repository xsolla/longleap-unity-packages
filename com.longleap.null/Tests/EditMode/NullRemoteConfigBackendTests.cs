using System.Threading.Tasks;
using LongLeap.Null;
using NUnit.Framework;

namespace LongLeap.Null.Tests
{
    [TestFixture]
    public class NullRemoteConfigBackendTests
    {
        [Test]
        public async Task FetchAsync_ReturnsFailure()
        {
            var backend = new NullRemoteConfigBackend();
            var result = await backend.FetchAsync();

            Assert.IsFalse(result.IsSuccess, "Null-бэкенд должен возвращать IsSuccess=false");
        }

        [Test]
        public async Task FetchAsync_ParametersAreNull()
        {
            var backend = new NullRemoteConfigBackend();
            var result = await backend.FetchAsync();

            Assert.IsNull(result.Parameters, "Null-бэкенд должен возвращать Parameters=null");
        }

        [Test]
        public async Task FetchAsync_ExperimentsAreNull()
        {
            var backend = new NullRemoteConfigBackend();
            var result = await backend.FetchAsync();

            Assert.IsNull(result.Experiments, "Null-бэкенд должен возвращать Experiments=null");
        }

        [Test]
        public void FetchAsync_CompletesImmediately()
        {
            var backend = new NullRemoteConfigBackend();
            var task = backend.FetchAsync();

            Assert.IsTrue(task.IsCompleted, "Null-бэкенд должен завершать Task синхронно, без асинхронной задержки");
        }
    }
}
