using LongLeap.Settings.UI;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace LongLeap.Settings.Tests
{
    public class SettingsKeyRowRebindTests
    {
        private SettingsService _service;
        private TestKeysGroup _keys;

        [SetUp]
        public void SetUp()
        {
            _service = new SettingsService(null);
            _keys = new TestKeysGroup();
            _service.Register(_keys);
        }

        [Test]
        public void FreeKey_IsAssigned()
        {
            SettingsKeyRow.Rebind(_service, _keys.Jump, Key.J);

            Assert.AreEqual(Key.J, _keys.Jump.Value);
            Assert.AreEqual(Key.F, _keys.Fire.Value);
        }

        [Test]
        public void UsedKey_IsSwapped()
        {
            SettingsKeyRow.Rebind(_service, _keys.Jump, Key.F);

            Assert.AreEqual(Key.F, _keys.Jump.Value);
            Assert.AreEqual(Key.Space, _keys.Fire.Value);
        }

        [Test]
        public void KeyUsedInAnotherGroup_IsSwapped()
        {
            var other = new TestKeysGroup("settings.other_keys", jump: Key.E, fire: Key.Q);
            _service.Register(other);

            SettingsKeyRow.Rebind(_service, _keys.Jump, Key.E);

            Assert.AreEqual(Key.E, _keys.Jump.Value);
            Assert.AreEqual(Key.Space, other.Jump.Value);
        }

        [Test]
        public void SameKey_ChangesNothing()
        {
            int calls = 0;
            _service.OnChanged += _ => calls++;

            SettingsKeyRow.Rebind(_service, _keys.Jump, Key.Space);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void NoneKey_IsIgnored()
        {
            SettingsKeyRow.Rebind(_service, _keys.Jump, Key.None);

            Assert.AreEqual(Key.Space, _keys.Jump.Value);
        }
    }
}
