using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace LongLeap.Core.Tests
{
    // Writes real editor PlayerPrefs under its own key and removes it afterwards.
    [TestFixture]
    public sealed class PlayerPrefsPersistentStorageTests
    {
        private const string Key = "longleap.core.tests.storage";

        private PlayerPrefsPersistentStorage _storage;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(Key);
            _storage = new PlayerPrefsPersistentStorage();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(Key);
        }

        [Test]
        public async Task MissingKey_LoadsNull()
        {
            Assert.That(await _storage.HasKeyAsync(Key), Is.False);
            Assert.That(await _storage.LoadAsync(Key), Is.Null);
        }

        [Test]
        public async Task SaveLoadDelete_RoundTrips()
        {
            await _storage.SaveAsync(Key, "{\"a\":1}");

            Assert.That(await _storage.HasKeyAsync(Key), Is.True);
            Assert.That(await _storage.LoadAsync(Key), Is.EqualTo("{\"a\":1}"));

            await _storage.DeleteAsync(Key);

            Assert.That(await _storage.HasKeyAsync(Key), Is.False);
            Assert.That(await _storage.LoadAsync(Key), Is.Null);
        }
    }
}
