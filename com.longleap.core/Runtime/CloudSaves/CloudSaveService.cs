using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace LongLeap.Core.CloudSaves
{
    public sealed class CloudSaveService : ICloudSaveService, ICloudSaveBulkAccess
    {
        private const string DirtyKey = "cloudsave_dirty";

        // Список ВСЕХ ключей, у которых есть локальная копия.
        //
        // Нужен потому, что перечислять ключи негде: ни IPlayerPrefs, ни
        // PlayerPrefs так не умеют. Без него локальное зеркало нельзя вычистить
        // целиком, а забытый ключ с более свежей меткой при следующей загрузке
        // победил бы облачный — то есть прогресс одной личности протёк бы в
        // другую.
        private const string IndexKey = "cloudsave_index";
        private const string TsLocalSuffix = "_ts";

        private readonly ICloudSaveProvider _provider;
        private readonly IPlayerPrefs _prefs;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private readonly Dictionary<string, long> _timestamps = new Dictionary<string, long>();
        private readonly HashSet<string> _dirtyKeys = new HashSet<string>();

        // Что лежит в PlayerPrefs — включая ключи, которые в этой сессии никто
        // не читал, поэтому в _cache их нет.
        private readonly HashSet<string> _knownKeys = new HashSet<string>();

        // Замок состояния: кеш, метки времени, набор «грязных» ключей и PlayerPrefs.
        // Под ним делается только работа с памятью и локальным хранилищем, то есть
        // мгновенная. Сеть под ним держать НЕЛЬЗЯ — см. PushDirtyKeysAsync.
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // Отдельный замок на сетевые отправки. Нужен, чтобы две досылки не разошлись
        // на одном наборе ключей, но при этом не задевал читателей кеша.
        private readonly SemaphoreSlim _pushLock = new SemaphoreSlim(1, 1);

        private bool _initialized;
        private bool _disposed;

        public CloudSaveService(ICloudSaveProvider provider, IPlayerPrefs playerPrefs)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _prefs = playerPrefs ?? throw new ArgumentNullException(nameof(playerPrefs));
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            await _lock.WaitAsync();
            try
            {
                if (_initialized) return;

                LoadDirtyKeys();
                LoadKnownKeys();

                foreach (var k in new List<string>(_dirtyKeys))
                {
                    var val = _prefs.GetString(CloudKey(k), null);
                    if (val != null)
                    {
                        _cache[k] = val;
                        _timestamps[k] = GetLocalTimestamp(k);
                        Remember(k);
                    }
                }

                var cloudData = await _provider.LoadAllAsync();
                Debug.Log($"{nameof(CloudSaveService)}: loaded {cloudData.Count} keys from cloud, {_dirtyKeys.Count} dirty locally");

                foreach (var kvp in cloudData)
                {
                    var key = kvp.Key;
                    var (value, cloudTs) = DeserializeCloudEntry(kvp.Value);
                    if (value == null) continue;

                    long localTs = _timestamps.TryGetValue(key, out var lts) ? lts : GetLocalTimestamp(key);

                    if (cloudTs > localTs)
                    {
                        _cache[key] = value;
                        _timestamps[key] = cloudTs;
                        _prefs.SetString(CloudKey(key), value);
                        _prefs.SetString(TsKey(key), cloudTs.ToString());
                        _dirtyKeys.Remove(key);
                        Remember(key);
                    }
                    else if (!_cache.ContainsKey(key))
                    {
                        _cache[key] = value;
                        _timestamps[key] = cloudTs;
                        if (cloudTs > 0)
                            _prefs.SetString(TsKey(key), cloudTs.ToString());
                        _prefs.SetString(CloudKey(key), value);
                        Remember(key);
                    }
                    else if (localTs > cloudTs)
                    {
                        _dirtyKeys.Add(key);
                    }
                }

                SaveDirtyKeys();
                SaveKnownKeys();

                _initialized = true;
            }
            finally
            {
                _lock.Release();
            }

            // Досылку локальных изменений — уже вне замка: это сеть.
            await PushDirtyKeysAsync();
        }

        public async Task SetStringAsync(CloudSaveKey key, string value)
        {
            ThrowIfDisposed();

            var keyStr = key.Value;

            // Под замком — только память и PlayerPrefs. Это и есть настоящая запись:
            // сервис local-first, значение уже сохранено и уже читается всеми, кто
            // спросит, независимо от того, доехало ли оно до облака.
            await _lock.WaitAsync();
            try
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                _cache[keyStr] = value;
                _timestamps[keyStr] = ts;
                _dirtyKeys.Add(keyStr);

                _prefs.SetString(CloudKey(keyStr), value);
                _prefs.SetString(TsKey(keyStr), ts.ToString());

                if (Remember(keyStr))
                    SaveKnownKeys();

                SaveDirtyKeys();
                _prefs.Save();
            }
            finally
            {
                _lock.Release();
            }

            Debug.Log($"{nameof(CloudSaveService)}: saved '{keyStr}'");

            // Отправка в облако — ВНЕ замка. Раньше она стояла внутри, и это было
            // самой дорогой ошибкой всей загрузки: один сетевой PUT (у него свой
            // таймаут в 10 с) держал замок, а за ним в очередь вставали ВСЕ читатели
            // кеша. На холодном запуске из-за одной фоновой записи профиля молча
            // проваливались по таймауту настройки, статы, ежедневные награды и
            // ачивки — при том, что каждому из них нужно было лишь чтение из памяти.
            //
            // Ждать отправку по-прежнему можно (метод её awaits) — просто теперь это
            // ожидание касается только того, кто сам решил дождаться.
            await PushDirtyKeysAsync();
        }

        public async Task<string> GetStringAsync(CloudSaveKey key)
        {
            ThrowIfDisposed();
            await _lock.WaitAsync();
            try
            {
                var keyStr = key.Value;
                if (_cache.TryGetValue(keyStr, out string value))
                    return value;

                value = _prefs.GetString(CloudKey(keyStr), null);
                if (value != null)
                {
                    _cache[keyStr] = value;
                    _timestamps[keyStr] = GetLocalTimestamp(keyStr);

                    if (Remember(keyStr))
                        SaveKnownKeys();
                }

                Debug.Log($"{nameof(CloudSaveService)}: fetched '{keyStr}' — {(value != null ? "found" : "null")}");
                return value;
            }
            finally
            {
                _lock.Release();
            }
        }

        // Снимок всего, что сервис считает текущим состоянием: значения плюс их
        // метки времени.
        //
        // Только чтение памяти под общим замком — ни записи, ни обращения к
        // провайдеру. Именно поэтому им можно показать игроку содержимое сейва,
        // ничего за него не решив: InitializeAsync, в отличие от этого метода,
        // выбирает победителя по каждому ключу и тут же затирает проигравшего.
        public async Task<IReadOnlyDictionary<string, CloudSaveEntry>> SnapshotAsync()
        {
            ThrowIfDisposed();
            await _lock.WaitAsync();
            try
            {
                var snapshot = new Dictionary<string, CloudSaveEntry>(_cache.Count);

                foreach (var pair in _cache)
                {
                    long ts = _timestamps.TryGetValue(pair.Key, out long known)
                        ? known
                        : GetLocalTimestamp(pair.Key);

                    snapshot[pair.Key] = new CloudSaveEntry(pair.Value, ts);
                }

                return snapshot;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> HasKeyAsync(CloudSaveKey key)
        {
            ThrowIfDisposed();
            await _lock.WaitAsync();
            try
            {
                var keyStr = key.Value;
                return _cache.ContainsKey(keyStr) || _prefs.HasKey(CloudKey(keyStr));
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteKeyAsync(CloudSaveKey key)
        {
            ThrowIfDisposed();

            await _lock.WaitAsync();
            try
            {
                var keyStr = key.Value;
                _cache.Remove(keyStr);
                _timestamps.Remove(keyStr);
                _dirtyKeys.Add(keyStr);

                _prefs.DeleteKey(CloudKey(keyStr));
                _prefs.DeleteKey(TsKey(keyStr));
                _knownKeys.Remove(keyStr);
                SaveDirtyKeys();
                SaveKnownKeys();
                _prefs.Save();
            }
            finally
            {
                _lock.Release();
            }

            await PushDirtyKeysAsync();
        }

        public async Task DeleteAllAsync()
        {
            ThrowIfDisposed();

            await _lock.WaitAsync();
            try
            {
                ForgetLocalLocked();
                _prefs.Save();
            }
            finally
            {
                _lock.Release();
            }

            // Тоже вне замка: удаление в облаке — такой же сетевой запрос.
            await _provider.DeleteAllAsync();
        }

        // Забыть локальное зеркало, НЕ удаляя ничего в облаке.
        //
        // Не путать с DeleteAllAsync: тот стирает сейв у владельца. Здесь
        // наоборот — сейв в облаке остаётся неприкосновенным, уходит только его
        // локальная копия. Нужно при смене личности: гостевое зеркало не имеет
        // права остаться в аккаунте и наоборот, иначе следующая же досылка
        // записала бы чужие значения владельцу.
        //
        // Сервис после этого считается неинициализированным — следующий
        // InitializeAsync поднимет всё заново, уже из ветви новой личности.
        public async Task ResetLocalCacheAsync()
        {
            ThrowIfDisposed();

            await _lock.WaitAsync();
            try
            {
                ForgetLocalLocked();
                _prefs.Save();

                _initialized = false;
            }
            finally
            {
                _lock.Release();
            }

            Debug.Log($"{nameof(CloudSaveService)}: local mirror cleared, cloud untouched");
        }

        // Общая часть DeleteAllAsync и ResetLocalCacheAsync. Зовётся ТОЛЬКО под
        // _lock.
        //
        // Чистим по объединению кеша и указателя: в кеше нет ключей, которых
        // никто не читал в этой сессии, а в указателе они есть — ровно для этого
        // он и ведётся.
        private void ForgetLocalLocked()
        {
            var keys = new HashSet<string>(_knownKeys);

            foreach (var key in _cache.Keys)
                keys.Add(key);

            foreach (var key in keys)
            {
                _prefs.DeleteKey(CloudKey(key));
                _prefs.DeleteKey(TsKey(key));
            }

            _cache.Clear();
            _timestamps.Clear();
            _dirtyKeys.Clear();
            _knownKeys.Clear();

            _prefs.DeleteKey(DirtyKey);
            _prefs.DeleteKey(IndexKey);
        }

        public void Dispose()
        {
            _disposed = true;
            _lock.Dispose();
            _pushLock.Dispose();
        }

        // Досылка «грязных» ключей в облако. Единственное место сервиса, где идёт
        // сеть, и потому единственное, что не имеет права держать _lock: пока едет
        // запрос (у HTTP-клиента таймаут 10 с), кеш обязан оставаться читаемым.
        //
        // Вместо этого сеть накрыта своим замком _pushLock — он серилизует досылки
        // между собой, не задевая читателей, — а состояние берётся и правится
        // короткими заходами под _lock.
        private async Task PushDirtyKeysAsync()
        {
            // Отправка живёт дольше вызывающего: её запускают и не ждут (сохранение
            // профиля, отметка ачивки), а сцена между тем перезагружается и сервис
            // получает Dispose. Возвращаться к уже уничтоженным семафорам нельзя —
            // ловим это здесь, иначе в лог валится ObjectDisposedException на каждом
            // рестарте, хотя терять нечего: локально всё уже сохранено.
            try
            {
                await PushDirtyKeysCoreAsync();
            }
            catch (ObjectDisposedException)
            {
                Debug.Log($"{nameof(CloudSaveService)}: push aborted — the service is already disposed.");
            }
        }

        private async Task PushDirtyKeysCoreAsync()
        {
            if (_disposed) return;

            await _pushLock.WaitAsync();
            try
            {
                // Список снимаем копией: во время отправки кто-то может добавить
                // новый ключ, а перечислять изменяемый набор нельзя.
                List<string> pending;
                await _lock.WaitAsync();
                try
                {
                    pending = new List<string>(_dirtyKeys);
                }
                finally
                {
                    _lock.Release();
                }

                if (pending.Count == 0) return;

                Debug.Log($"{nameof(CloudSaveService)}: pushing {pending.Count} dirty keys...");

                var succeeded = new List<string>();

                foreach (var key in pending)
                {
                    string value;
                    bool hasValue;
                    long ts;

                    await _lock.WaitAsync();
                    try
                    {
                        hasValue = _cache.TryGetValue(key, out value);
                        ts = _timestamps.TryGetValue(key, out var t) ? t : 0;
                    }
                    finally
                    {
                        _lock.Release();
                    }

                    try
                    {
                        if (hasValue)
                            await _provider.SetStringAsync(new CloudSaveKey(key), SerializeCloudEntry(value, ts));
                        else
                            await _provider.DeleteKeyAsync(new CloudSaveKey(key));

                        succeeded.Add(key);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"{nameof(CloudSaveService)}: failed to sync key '{key}': {ex.Message}");
                    }
                }

                if (succeeded.Count == 0) return;

                await _lock.WaitAsync();
                try
                {
                    foreach (var key in succeeded)
                        _dirtyKeys.Remove(key);

                    SaveDirtyKeys();
                }
                finally
                {
                    _lock.Release();
                }

                Debug.Log($"{nameof(CloudSaveService)}: pushed {succeeded.Count} keys");
            }
            finally
            {
                _pushLock.Release();
            }
        }

        // Формат конверта живёт в CloudSaveEnvelope: его же читает сводка чужого
        // сейва, и два разных парсера рано или поздно разошлись бы.
        private static string SerializeCloudEntry(string value, long ts) =>
            CloudSaveEnvelope.Serialize(value, ts);

        private static (string value, long ts) DeserializeCloudEntry(string raw) =>
            CloudSaveEnvelope.Parse(raw);

        private long GetLocalTimestamp(string key)
        {
            var tsStr = _prefs.GetString(TsKey(key), null);
            return long.TryParse(tsStr, out var ts) ? ts : 0;
        }

        private void LoadDirtyKeys()
        {
            string dirty = _prefs.GetString(DirtyKey, "");
            if (string.IsNullOrEmpty(dirty)) return;
            try
            {
                var keys = JsonConvert.DeserializeObject<List<string>>(dirty);
                if (keys != null)
                    foreach (var k in keys)
                        _dirtyKeys.Add(k);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(CloudSaveService)}: dirty keys deserialization error: {ex.Message}");
            }
        }

        private void SaveDirtyKeys()
        {
            string dirty = JsonConvert.SerializeObject(new List<string>(_dirtyKeys));
            _prefs.SetString(DirtyKey, dirty);
        }

        // true — ключ раньше не был известен, указатель пора переписать.
        //
        // Признак изменения, а не запись на месте: у статов и ачивок ключей
        // сотни, и переписывать весь список на каждое сохранение монеты было бы
        // заметно. Новые ключи появляются только на прогреве.
        private bool Remember(string key) => _knownKeys.Add(key);

        private void LoadKnownKeys()
        {
            string index = _prefs.GetString(IndexKey, "");
            if (string.IsNullOrEmpty(index)) return;
            try
            {
                var keys = JsonConvert.DeserializeObject<List<string>>(index);
                if (keys != null)
                {
                    foreach (var k in keys)
                    {
                        if (!string.IsNullOrEmpty(k))
                            _knownKeys.Add(k);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(CloudSaveService)}: key index deserialization error: {ex.Message}");
            }
        }

        private void SaveKnownKeys()
        {
            string index = JsonConvert.SerializeObject(new List<string>(_knownKeys));
            _prefs.SetString(IndexKey, index);
        }

        private static string CloudKey(string key) => $"cloudsave_{key}";
        private static string TsKey(string key) => $"cloudsave{TsLocalSuffix}_{key}";

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CloudSaveService));
        }
    }
}
