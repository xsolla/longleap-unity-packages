using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Settings.Tests
{
    public class SettingsServiceTests
    {
        [Test]
        public void Register_DuplicateStorageKey_Throws()
        {
            var service = new SettingsService(null);
            service.Register(new TestGroup("same"));

            Assert.Throws<InvalidOperationException>(() => service.Register(new TestGroup("same")));
            Assert.AreEqual(1, service.Groups.Count);
        }

        [Test]
        public void SetValue_NewValue_FiresOnChangedOnce()
        {
            var service = new SettingsService(null);
            var group = new TestGroup();
            service.Register(group);

            int calls = 0;
            Setting changed = null;
            service.OnChanged += s => { calls++; changed = s; };

            service.SetValue(group.Flag, false);

            Assert.AreEqual(1, calls);
            Assert.AreSame(group.Flag, changed);
            Assert.IsFalse(group.Flag.Value);
        }

        [Test]
        public void SetValue_SameValueAfterCoerce_DoesNotFire()
        {
            var service = new SettingsService(null);
            var group = new TestGroup();
            service.Register(group);

            int calls = 0;
            service.OnChanged += _ => calls++;

            service.SetValue(group.Flag, group.Flag.Value); // same value
            service.SetValue(group.Volume, 5f);             // clamped to 1: changed
            service.SetValue(group.Volume, 7f);             // clamped to 1 again: unchanged

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void LoadAll_NullCloudSave_KeepsDefaults()
        {
            var service = new SettingsService(null);
            var group = new TestGroup();
            service.Register(group);

            service.LoadAllAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0.5f, group.Volume.Value);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("{}")]
        public void LoadAll_EmptyData_KeepsDefaults(string stored)
        {
            var cloud = new FakeCloudSave();
            cloud.Data["settings.test"] = stored;
            var service = new SettingsService(cloud);
            var group = new TestGroup();
            service.Register(group);

            service.LoadAllAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0.5f, group.Volume.Value);
            Assert.AreEqual(10, group.Count.Value);
        }

        [Test]
        public void LoadAll_FailingGroup_KeepsDefaultsAndLoadsOthers()
        {
            var cloud = new FakeCloudSave();
            cloud.Failing.Add("settings.bad");
            cloud.Data["settings.good"] = "0.25|42|False|High";
            var service = new SettingsService(cloud);
            var bad = new TestGroup("settings.bad");
            var good = new TestGroup("settings.good");
            service.Register(bad);
            service.Register(good);

            LogAssert.Expect(LogType.Warning, new Regex(@"settings\.bad"));
            service.LoadAllAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0.5f, bad.Volume.Value);
            Assert.AreEqual(0.25f, good.Volume.Value);
            Assert.AreEqual(42, good.Count.Value);
            Assert.IsFalse(good.Flag.Value);
            Assert.AreEqual(TestQuality.High, good.Quality.Value);
        }

        [Test]
        public void SaveAll_NullCloudSave_DoesNotThrow()
        {
            var service = new SettingsService(null);
            service.Register(new TestGroup());

            Assert.DoesNotThrow(() => service.SaveAllAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void SaveAll_WritesEachGroupUnderItsStorageKey_AndLoadRestoresIt()
        {
            var cloud = new FakeCloudSave();
            var first = new SettingsService(cloud);
            var a = new TestGroup("settings.a");
            var b = new TestGroup("settings.b");
            first.Register(a);
            first.Register(b);
            first.SetValue(b.Count, 33);
            first.SetValue(b.Quality, TestQuality.Low);

            first.SaveAllAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "settings.a", "settings.b" }, cloud.Writes);

            var second = new SettingsService(cloud);
            var loaded = new TestGroup("settings.b");
            second.Register(loaded);
            second.LoadAllAsync().GetAwaiter().GetResult();

            Assert.AreEqual(33, loaded.Count.Value);
            Assert.AreEqual(TestQuality.Low, loaded.Quality.Value);
        }
    }
}
