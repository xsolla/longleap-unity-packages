using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LongLeap.Achievements;
using LongLeap.Platform.Unity;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Platform.Unity.Tests
{
    [TestFixture]
    public class JsonAchievementProviderTests
    {
        private Dictionary<string, string> _files;

        [SetUp]
        public void Setup()
        {
            _files = new Dictionary<string, string>();
        }

        private void WriteManifest(params string[] files)
        {
            _files[JsonAchievementProvider.ManifestFileName] = JsonConvert.SerializeObject(new { files });
        }

        private void WriteFile(string fileName, string content)
        {
            _files[fileName] = content;
        }

        private Task<AchievementDefinition[]> Load()
        {
            var provider = new JsonAchievementProvider(
                name => _files.TryGetValue(name, out var text) ? text : null);
            return provider.LoadAsync();
        }

        [Test]
        public async Task Load_StatObjective_ParsesCorrectly()
        {
            WriteManifest("combat.json");
            WriteFile("combat.json", @"
{
  ""achievements"": [
    {
      ""id"": ""ach_kill_10"",
      ""group"": ""combat"",
      ""isHidden"": false,
      ""prerequisites"": [],
      ""objectives"": [
        { ""id"": ""obj_kill"", ""statPath"": ""kill"", ""statTarget"": 10 }
      ]
    }
  ]
}");

            var definitions = await Load();

            Assert.AreEqual(1, definitions.Length);
            var def = definitions[0];
            Assert.AreEqual("ach_kill_10", def.Id.Value);
            Assert.AreEqual("combat", def.Group.Value);
            Assert.IsFalse(def.IsHidden);
            Assert.AreEqual(1, def.Objectives.Length);
            Assert.AreEqual("obj_kill", def.Objectives[0].Id.Value);
            Assert.AreEqual("kill", def.Objectives[0].StatPath);
            Assert.AreEqual(10, def.Objectives[0].StatTarget);
        }

        [Test]
        public async Task Load_RequiredObjective_ParsesCorrectly()
        {
            WriteManifest("heroes.json");
            WriteFile("heroes.json", @"
{
  ""achievements"": [
    {
      ""id"": ""all_heroes"",
      ""group"": ""heroes"",
      ""objectives"": [
        { ""id"": ""obj_all"", ""required"": [""hero_monk"", ""hero_ninja""] }
      ]
    }
  ]
}");

            var definitions = await Load();

            Assert.AreEqual(1, definitions.Length);
            var obj = definitions[0].Objectives[0];
            Assert.AreEqual("obj_all", obj.Id.Value);
            Assert.IsNull(obj.StatPath);
            Assert.AreEqual(0, obj.StatTarget);
            Assert.AreEqual(2, obj.Required.Length);
            Assert.AreEqual("hero_monk", obj.Required[0].Value);
            Assert.AreEqual("hero_ninja", obj.Required[1].Value);
        }

        [Test]
        public async Task Load_TwoFiles_MergesInManifestOrder()
        {
            WriteManifest("combat.json", "heroes.json");

            WriteFile("combat.json", @"
{
  ""achievements"": [
    {
      ""id"": ""ach_kill"",
      ""group"": ""combat"",
      ""objectives"": [
        { ""id"": ""obj_kill"", ""statPath"": ""kill"", ""statTarget"": 10 }
      ]
    }
  ]
}");

            WriteFile("heroes.json", @"
{
  ""achievements"": [
    {
      ""id"": ""all_heroes"",
      ""group"": ""heroes"",
      ""objectives"": [
        { ""id"": ""obj_all"", ""required"": [""hero_monk""] }
      ]
    }
  ]
}");

            var definitions = await Load();

            Assert.AreEqual(new[] { "ach_kill", "all_heroes" }, definitions.Select(d => d.Id.Value).ToArray());
        }

        [Test]
        public async Task Load_WithPrerequisites_ParsesCorrectly()
        {
            WriteManifest("test.json");
            WriteFile("test.json", @"
{
  ""achievements"": [
    {
      ""id"": ""ach_kill_100"",
      ""group"": ""combat"",
      ""prerequisites"": [""ach_kill_10""],
      ""objectives"": [
        { ""id"": ""obj_kill"", ""statPath"": ""kill"", ""statTarget"": 100 }
      ]
    }
  ]
}");

            var definitions = await Load();

            Assert.AreEqual(1, definitions.Length);
            Assert.AreEqual(1, definitions[0].Prerequisites.Length);
            Assert.AreEqual("ach_kill_10", definitions[0].Prerequisites[0].Value);
        }

        [Test]
        public async Task Load_WithHidden_ParsesCorrectly()
        {
            WriteManifest("test.json");
            WriteFile("test.json", @"
{
  ""achievements"": [
    {
      ""id"": ""hidden_ach"",
      ""group"": ""test"",
      ""isHidden"": true,
      ""objectives"": [
        { ""id"": ""o"", ""statPath"": ""kill"", ""statTarget"": 1 }
      ]
    }
  ]
}");

            var definitions = await Load();

            Assert.IsTrue(definitions[0].IsHidden);
        }

        [Test]
        public async Task CorruptJsonFile_Skipped_OtherFilesLoaded()
        {
            WriteManifest("valid.json", "corrupt.json");
            WriteFile("valid.json", @"
{
  ""achievements"": [
    {
      ""id"": ""ach_valid"",
      ""group"": ""test"",
      ""objectives"": [
        { ""id"": ""o"", ""statPath"": ""kill"", ""statTarget"": 1 }
      ]
    }
  ]
}");
            WriteFile("corrupt.json", "{ this is not valid json!!! }");

            var definitions = await Load();

            Assert.AreEqual(1, definitions.Length);
            Assert.AreEqual("ach_valid", definitions[0].Id.Value);
        }

        [Test]
        public async Task MissingListedFile_LogsError_OtherFilesLoaded()
        {
            WriteManifest("valid.json", "missing.json");
            WriteFile("valid.json", @"
{
  ""achievements"": [
    { ""id"": ""ach_valid"", ""group"": ""test"", ""objectives"": [] }
  ]
}");

            LogAssert.Expect(LogType.Error, new Regex("missing\\.json"));

            var definitions = await Load();

            Assert.AreEqual(1, definitions.Length);
        }

        [Test]
        public async Task MissingManifest_LogsError_ReturnsEmptyArray()
        {
            LogAssert.Expect(LogType.Error, new Regex("manifest\\.json not found"));

            var definitions = await Load();

            Assert.IsNotNull(definitions);
            Assert.IsEmpty(definitions);
        }

        // Ровно то, что случилось на GamePix: на месте манифеста пришла HTML-страница.
        [Test]
        public async Task CorruptManifest_LogsError_ReturnsEmptyArray()
        {
            WriteFile(JsonAchievementProvider.ManifestFileName, "<!DOCTYPE html><html></html>");

            LogAssert.Expect(LogType.Error, new Regex("Failed to parse manifest\\.json"));

            var definitions = await Load();

            Assert.IsNotNull(definitions);
            Assert.IsEmpty(definitions);
        }

        [Test]
        public async Task EmptyManifest_ReturnsEmptyArray()
        {
            WriteManifest();

            var definitions = await Load();

            Assert.IsNotNull(definitions);
            Assert.IsEmpty(definitions);
        }

        [Test]
        public async Task Load_NoTitleDescription_DoesNotFail()
        {
            WriteManifest("test.json");
            WriteFile("test.json", @"
{
  ""achievements"": [
    {
      ""id"": ""ach_test"",
      ""group"": ""test"",
      ""objectives"": [
        { ""id"": ""o"", ""statPath"": ""kill"", ""statTarget"": 1 }
      ]
    }
  ]
}");

            var definitions = await Load();

            Assert.AreEqual(1, definitions.Length);
            Assert.AreEqual("ach_test", definitions[0].Id.Value);
        }
    }
}
