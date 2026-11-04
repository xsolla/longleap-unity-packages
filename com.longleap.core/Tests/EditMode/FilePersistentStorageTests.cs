using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.Core.Tests
{
    // Each test gets its own folder under the system temp directory.
    [TestFixture]
    public sealed class FilePersistentStorageTests
    {
        private string _directory;
        private FilePersistentStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "LongLeapCoreTests_" + Guid.NewGuid().ToString("N"));
            _storage = new FilePersistentStorage(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Constructor_NullDirectory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new FilePersistentStorage(null));
        }

        [Test]
        public async Task MissingKey_LoadsNull()
        {
            Assert.That(await _storage.HasKeyAsync("profile"), Is.False);
            Assert.That(await _storage.LoadAsync("profile"), Is.Null);
        }

        [Test]
        public async Task Save_CreatesDirectory_AndRoundTrips()
        {
            await _storage.SaveAsync("profile", "{\"level\":3}");

            Assert.That(File.Exists(Path.Combine(_directory, "profile.json")), Is.True);
            Assert.That(await _storage.HasKeyAsync("profile"), Is.True);
            Assert.That(await _storage.LoadAsync("profile"), Is.EqualTo("{\"level\":3}"));
        }

        [Test]
        public async Task Save_Overwrites_AndLeavesNoTempFile()
        {
            await _storage.SaveAsync("profile", "old");
            await _storage.SaveAsync("profile", "new");

            Assert.That(await _storage.LoadAsync("profile"), Is.EqualTo("new"));
            Assert.That(File.Exists(Path.Combine(_directory, "profile.json.tmp")), Is.False);
        }

        [Test]
        public async Task Delete_RemovesKey_AndIgnoresMissingKey()
        {
            await _storage.SaveAsync("profile", "data");

            await _storage.DeleteAsync("profile");
            await _storage.DeleteAsync("never_saved");

            Assert.That(await _storage.HasKeyAsync("profile"), Is.False);
        }
    }
}
