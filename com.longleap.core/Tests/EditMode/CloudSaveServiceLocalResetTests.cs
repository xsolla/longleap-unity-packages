using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;

namespace LongLeap.Core.Tests
{
    // Указатель ключей и локальный сброс зеркала.
    //
    // Оба нужны смене личности: без указателя вычистить зеркало целиком нельзя
    // (перечислять PlayerPrefs нечем), а забытый ключ с более свежей меткой при
    // следующей загрузке победил бы облачный — то есть прогресс одной личности
    // протёк бы в другую.
    [TestFixture]
    public class CloudSaveServiceLocalResetTests
    {
        private sealed class FakePlayerPrefs : IPlayerPrefs
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();

            public string GetString(string key, string defaultValue = "") =>
                Values.TryGetValue(key, out string v) ? v : defaultValue;

            public int GetInt(string key, int defaultValue = 0) => defaultValue;
            public float GetFloat(string key, float defaultValue = 0f) => defaultValue;
            public void SetString(string key, string value) => Values[key] = value;
            public void SetInt(string key, int value) { }
            public void SetFloat(string key, float value) { }
            public bool HasKey(string key) => Values.ContainsKey(key);
            public void DeleteKey(string key) => Values.Remove(key);
            public void Save() { }
        }

        private sealed class FakeProvider : ICloudSaveProvider
        {
            public readonly Dictionary<string, string> Cloud = new Dictionary<string, string>();

            public int DeleteAllCalls;
            public int DeleteKeyCalls;

            public Task SetStringAsync(CloudSaveKey key, string value)
            {
                Cloud[key.Value] = value;
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(CloudSaveKey key) =>
                Task.FromResult(Cloud.TryGetValue(key.Value, out string v) ? v : null);

            public Task<bool> HasKeyAsync(CloudSaveKey key) => Task.FromResult(Cloud.ContainsKey(key.Value));

            public Task DeleteKeyAsync(CloudSaveKey key)
            {
                DeleteKeyCalls++;
                Cloud.Remove(key.Value);
                return Task.CompletedTask;
            }

            public Task<Dictionary<string, string>> LoadAllAsync() =>
                Task.FromResult(new Dictionary<string, string>(Cloud));

            public Task DeleteAllAsync()
            {
                DeleteAllCalls++;
                Cloud.Clear();
                return Task.CompletedTask;
            }
        }

        private FakePlayerPrefs _prefs;
        private FakeProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _prefs = new FakePlayerPrefs();
            _provider = new FakeProvider();
        }

        private CloudSaveService NewService() => new CloudSaveService(_provider, _prefs);

        [Test]
        public async Task ResetLocalCache_ClearsMirrorOfKeysWrittenThisSession()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                Assert.AreEqual("250", _prefs.GetString("cloudsave_wallet", null));

                await service.ResetLocalCacheAsync();

                Assert.IsNull(_prefs.GetString("cloudsave_wallet", null));
                Assert.IsNull(_prefs.GetString("cloudsave_ts_wallet", null));
            }
        }

        // Главное, ради чего указатель и заведён: ключ, который в ЭТОЙ сессии
        // никто не читал и не писал, в кеше отсутствует — и всё равно обязан
        // уйти из зеркала.
        [Test]
        public async Task ResetLocalCache_ClearsKeysNobodyTouchedThisSession()
        {
            using (CloudSaveService first = NewService())
            {
                await first.InitializeAsync();
                await first.SetStringAsync(new CloudSaveKey("wallet"), "250");
                await first.SetStringAsync(new CloudSaveKey("heroes_owned"), "[\"monk\"]");
            }

            // Новый сервис ничего не читает — только поднимает указатель.
            using (CloudSaveService second = NewService())
            {
                await second.InitializeAsync();
                await second.ResetLocalCacheAsync();
            }

            Assert.IsNull(_prefs.GetString("cloudsave_wallet", null));
            Assert.IsNull(_prefs.GetString("cloudsave_heroes_owned", null));
            Assert.IsNull(_prefs.GetString("cloudsave_ts_heroes_owned", null));
            Assert.IsNull(_prefs.GetString("cloudsave_index", null));
            Assert.IsNull(_prefs.GetString("cloudsave_dirty", null));
        }

        // Сброс — ЛОКАЛЬНЫЙ. Тронув облако, он отобрал бы у владельца сейв,
        // который мы как раз собирались оставить нетронутым.
        [Test]
        public async Task ResetLocalCache_DoesNotTouchProvider()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                await service.ResetLocalCacheAsync();

                Assert.AreEqual(0, _provider.DeleteAllCalls);
                Assert.AreEqual(0, _provider.DeleteKeyCalls);
                Assert.AreEqual("250", CloudSaveEnvelope.Parse(_provider.Cloud["wallet"]).value);
            }
        }

        // После сброса сервис снова не инициализирован: следующая загрузка
        // должна поднять всё заново, уже из ветви новой личности.
        [Test]
        public async Task ResetLocalCache_ThenInitialize_RestoresFromCloud()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                await service.ResetLocalCacheAsync();
                await service.InitializeAsync();

                Assert.AreEqual("250", await service.GetStringAsync(new CloudSaveKey("wallet")));
            }
        }

        [Test]
        public async Task DeleteAll_AlsoClearsTheIndex()
        {
            using (CloudSaveService service = NewService())
            {
                await service.InitializeAsync();
                await service.SetStringAsync(new CloudSaveKey("wallet"), "250");

                await service.DeleteAllAsync();

                Assert.IsNull(_prefs.GetString("cloudsave_index", null));
                Assert.AreEqual(1, _provider.DeleteAllCalls, "DeleteAll обязан дойти до облака");
            }
        }

        // Указатель обязан переживать перезапуск: иначе на следующей сессии он
        // знал бы только то, что успели тронуть, и смысл его пропал бы.
        [Test]
        public async Task Index_SurvivesServiceRestart()
        {
            using (CloudSaveService first = NewService())
            {
                await first.InitializeAsync();
                await first.SetStringAsync(new CloudSaveKey("wallet"), "250");
            }

            string index = _prefs.GetString("cloudsave_index", null);

            Assert.IsNotNull(index);
            Assert.IsTrue(index.Contains("wallet"), "в указателе должен быть записанный ключ");
        }
    }
}
