using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.RemoteConfig.Tests
{
    // Typed getters: parsing rules, defaults and warnings.
    [TestFixture]
    public sealed class RemoteConfigServiceGetterTests
    {
        private FakeRemoteConfigProvider _provider;
        private RemoteConfigService _service;

        [SetUp]
        public void SetUp()
        {
            _provider = new FakeRemoteConfigProvider();
            _service = new RemoteConfigService(_provider, new InMemoryPlayerPrefs());
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        private Task Serve(params (string key, string value)[] parameters)
        {
            _provider.Result = FakeRemoteConfigProvider.Success(parameters);
            return _service.FetchAsync();
        }

        [Test]
        public async Task Getters_ParseValidValues()
        {
            await Serve(("str", "hello"), ("int", "42"), ("long", "9000000000"), ("float", "3.14"), ("double", "2.5"), ("bool", "true"));

            Assert.AreEqual("hello", _service.GetString("str", "def"));
            Assert.AreEqual(42, _service.GetInt("int", 0));
            Assert.AreEqual(9000000000L, _service.GetLong("long", 0));
            Assert.AreEqual(3.14f, _service.GetFloat("float", 0f));
            Assert.AreEqual(2.5d, _service.GetDouble("double", 0d));
            Assert.AreEqual(true, _service.GetBool("bool", false));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("true", true)]
        [TestCase(" TRUE ", true)]
        [TestCase("1", true)]
        [TestCase("yes", true)]
        [TestCase("on", true)]
        [TestCase("false", false)]
        [TestCase("0", false)]
        [TestCase("No", false)]
        [TestCase("off", false)]
        public async Task GetBool_AcceptsFirebaseTokens(string raw, bool expected)
        {
            await Serve(("flag", raw));

            Assert.IsTrue(_service.TryGetBool("flag", out var value));
            Assert.AreEqual(expected, value);
        }

        [TestCase("42", 42)]
        [TestCase(" 42 ", 42)]
        [TestCase("42.0", 42)]
        [TestCase("-7", -7)]
        public async Task GetInt_AcceptsWholeNumbers(string raw, int expected)
        {
            await Serve(("n", raw));

            Assert.IsTrue(_service.TryGetInt("n", out var value));
            Assert.AreEqual(expected, value);
        }

        [TestCase("42.5")]
        [TestCase("banana")]
        [TestCase("3000000000")]
        [TestCase("")]
        public async Task TryGetInt_RejectsInvalidValues(string raw)
        {
            await Serve(("n", raw));

            Assert.IsFalse(_service.TryGetInt("n", out _));
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("1,5")]
        public async Task TryGetFloat_RejectsNonFiniteAndCommaDecimals(string raw)
        {
            await Serve(("f", raw));

            Assert.IsFalse(_service.TryGetFloat("f", out _));
            Assert.IsFalse(_service.TryGetDouble("f", out _));
        }

        [Test]
        public async Task GetFloat_IgnoresCurrentCulture()
        {
            await Serve(("f", "3.14"));
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                Assert.AreEqual(3.14f, _service.GetFloat("f", 0f));
                Assert.AreEqual(3.14d, _service.GetDouble("f", 0d));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        private sealed class Wave
        {
            public int Count;
            public List<string> Enemies;
        }

        [Test]
        public async Task GetJson_DeserializesOrFallsBack()
        {
            await Serve(("wave", "{\"Count\":3,\"Enemies\":[\"orc\",\"bat\"]}"), ("broken", "{not json"), ("nothing", "null"));

            var wave = _service.GetJson<Wave>("wave", null);
            Assert.AreEqual(3, wave.Count);
            CollectionAssert.AreEqual(new[] { "orc", "bat" }, wave.Enemies);

            Assert.IsFalse(_service.TryGetJson<Wave>("broken", out _));
            LogAssert.Expect(LogType.Warning, new Regex("'broken'.*not a valid Wave"));
            Assert.IsNull(_service.GetJson<Wave>("broken", null));

            var fallback = new Wave { Count = 1 };
            Assert.IsFalse(_service.TryGetJson<Wave>("nothing", out _), "JSON null is not a value");
            LogAssert.Expect(LogType.Warning, new Regex("'nothing'='null' is not a valid Wave"));
            Assert.AreSame(fallback, _service.GetJson("nothing", fallback));
        }

        [Test]
        public async Task Get_BadValue_ReturnsDefaultAndWarnsOncePerKeyAndType()
        {
            await Serve(("bad", "banana"));

            LogAssert.Expect(LogType.Warning, new Regex("RemoteConfigService: 'bad'='banana' is not a valid float"));
            Assert.AreEqual(3.14f, _service.GetFloat("bad", 3.14f));
            Assert.AreEqual(3.14f, _service.GetFloat("bad", 3.14f));

            LogAssert.Expect(LogType.Warning, new Regex("'bad'='banana' is not a valid int"));
            Assert.AreEqual(5, _service.GetInt("bad", 5));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public async Task Get_MissingKeyAfterFetch_WarnsOnce()
        {
            await Serve(("other", "value"));

            LogAssert.Expect(LogType.Warning, new Regex("RemoteConfigService: key 'missing' is not in Remote Config"));
            Assert.AreEqual(5f, _service.GetFloat("missing", 5f));
            Assert.AreEqual(5, _service.GetInt("missing", 5));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Get_MissingKeyWithoutServerValues_StaysSilent()
        {
            Assert.AreEqual("default", _service.GetString("missing", "default"));
            Assert.AreEqual(42, _service.GetInt("missing", 42));
            Assert.AreEqual(0, _service.ActiveExperiments.Count);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public async Task Get_MissingKeyWithWarningsOff_StaysSilent()
        {
            _service.Dispose();
            _service = new RemoteConfigService(_provider, new InMemoryPlayerPrefs(),
                new RemoteConfigOptions { WarnOnMissingKeys = false });
            await Serve(("other", "value"));

            Assert.AreEqual("default", _service.GetString("missing", "default"));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public async Task TryGet_NeverLogs()
        {
            await Serve(("bad", "banana"));

            Assert.IsFalse(_service.TryGetString("missing", out _));
            Assert.IsFalse(_service.TryGetInt("bad", out _));
            Assert.IsFalse(_service.TryGetBool("bad", out _));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public async Task Values_IsAReadOnlySnapshot()
        {
            await Serve(("key", "val"));

            Assert.IsTrue(_service.HasKey("key"));
            Assert.AreEqual("val", _service.Values["key"]);
            Assert.IsFalse(_service.Values is IDictionary<string, string> writable && !writable.IsReadOnly);
        }

        [Test]
        public void NullKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _service.GetString(null, "def"));
            Assert.Throws<ArgumentNullException>(() => _service.HasKey(null));
        }
    }
}
