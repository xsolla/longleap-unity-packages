using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Achievements.Tests
{
    // Parsing is covered by JsonAchievementProviderTests; these tests cover only
    // the Resources side that does not need assets in the package.
    [TestFixture]
    public class ResourcesAchievementProviderTests
    {
        [Test]
        public void Constructor_NullFolder_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ResourcesAchievementProvider(null));
        }

        [Test]
        public async Task MissingFolder_LogsError_ReturnsEmptyArray()
        {
            LogAssert.Expect(LogType.Error, new Regex("manifest\\.json not found"));

            var provider = new ResourcesAchievementProvider("LongLeapTests_NoSuchFolder/");
            var definitions = await provider.LoadAsync();

            Assert.IsNotNull(definitions);
            Assert.IsEmpty(definitions);
        }
    }
}
