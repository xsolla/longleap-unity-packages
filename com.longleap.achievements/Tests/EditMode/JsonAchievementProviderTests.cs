using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Achievements.Tests
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
        public async Task Load_StatAchievement_MapsAllFields()
        {
            WriteManifest("combat.json");
            WriteFile("combat.json", @"
{
  ""achievements"": [
    {
      ""id"": ""ach_kill_100"",
      ""group"": ""combat"",
      ""isHidden"": true,
      ""prerequisites"": [""ach_kill_10""],
      ""objectives"": [
        { ""id"": ""obj_kill"", ""statPath"": ""kill"", ""statTarget"": 100 }
      ]
    }
  ]
}");

            var definitions = await Load();

            Assert.AreEqual(1, definitions.Length);
            var def = definitions[0];
            Assert.AreEqual("ach_kill_100", def.Id.Value);
            Assert.AreEqual("combat", def.Group.Value);
            Assert.IsTrue(def.IsHidden);
            Assert.AreEqual(new[] { "ach_kill_10" }, def.Prerequisites.Select(p => p.Value).ToArray());
            Assert.AreEqual(1, def.Objectives.Length);
            Assert.AreEqual("obj_kill", def.Objectives[0].Id.Value);
            Assert.AreEqual("kill", def.Objectives[0].StatPath);
            Assert.AreEqual(100, def.Objectives[0].StatTarget);
            Assert.IsNull(def.Objectives[0].Required);
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
            Assert.IsFalse(definitions[0].IsHidden);
            Assert.IsEmpty(definitions[0].Prerequisites);
            var obj = definitions[0].Objectives[0];
            Assert.AreEqual("obj_all", obj.Id.Value);
            Assert.IsNull(obj.StatPath);
            Assert.AreEqual(0, obj.StatTarget);
            Assert.AreEqual(new[] { "hero_monk", "hero_ninja" }, obj.Required.Select(r => r.Value).ToArray());
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

            LogAssert.Expect(LogType.Warning, new Regex("corrupt\\.json"));

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

        // Exactly what happened on GamePix: an HTML page arrived in place of the manifest.
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
    }
}
