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

        // Index of EVERY key that has a local copy.
        //
        // Needed because keys cannot be enumerated: neither IPlayerPrefs nor
        // PlayerPrefs can do it. Without the index the local mirror could not be
        // cleared completely, and a forgotten key with a newer timestamp would
        // beat the cloud value on the next load — leaking one identity's
        // progress into another.
        private const string IndexKey = "cloudsave_index";
        private const string TsLocalSuffix = "_ts";

        // Pause before a batch push: writes that come in a row (a stats flush,
        // applying the save choice) accumulate during it and go out as one
        // request instead of one each.
        private const int BatchDebounceMs = 500;

        // Size cap for one batch, by value length. The server accepts bodies up
        // to 10 MB; JSON escaping inflates strings, so we keep a margin.
        private const long MaxBatchChars = 3L * 1024 * 1024;

        private readonly ICloudSaveProvider _provider;

        // The same provider if it can write in batches, otherwise null. Decides
        // the push mode — see ScheduleBatchPush.
        private readonly ICloudSaveBatchProvider _batch;
        private readonly IPlayerPrefs _prefs;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private readonly Dictionary<string, long> _timestamps = new Dictionary<string, long>();
        private readonly HashSet<string> _dirtyKeys = new HashSet<string>();

        // What is in PlayerPrefs, including keys nobody has read this session
        // (and that are therefore not in _cache).
        private readonly HashSet<string> _knownKeys = new HashSet<string>();

        // State lock: cache, timestamps, dirty set and PlayerPrefs. Only memory
        // and local storage work happens under it, i.e. it is held briefly.
        // Network calls must NEVER run under it — see PushDirtyKeysAsync.
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // Separate lock for network pushes, so two pushes do not race over the
        // same set of keys without blocking cache readers.
        private readonly SemaphoreSlim _pushLock = new SemaphoreSlim(1, 1);

        private bool _initialized;
        private bool _disposed;

        // The key index and the dirty set have been loaded from PlayerPrefs.
        //
        // Separate from _initialized: the service is used BEFORE InitializeAsync
        // too — at startup the save switch takes a snapshot from it and applies
        // the player's choice before the cloud is loaded. Without loading this
        // state, the very first write would overwrite the index and the dirty
        // set with its single key, and every other key would be lost for
        // clearing and pushing.
        private bool _stateLoaded;

        // A batch push is already scheduled and hasn't started yet.
        private bool _batchPushScheduled;

        public CloudSaveService(ICloudSaveProvider provider, IPlayerPrefs playerPrefs)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _prefs = playerPrefs ?? throw new ArgumentNullException(nameof(playerPrefs));
            _batch = provider as ICloudSaveBatchProvider;
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            await _lock.WaitAsync();
            try
            {
                if (_initialized) return;

                EnsureStateLoadedLocked();

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

            // Pushing local changes happens outside the lock: it is network.
            await PushDirtyKeysAsync();
        }

        public async Task SetStringAsync(CloudSaveKey key, string value)
        {
            ThrowIfDisposed();

            var keyStr = key.Value;

            // Only memory and PlayerPrefs under the lock. This IS the write: the
            // service is local-first, so the value is saved and readable by
            // everyone from here on, whether or not it has reached the cloud.
            await _lock.WaitAsync();
            try
            {
                EnsureStateLoadedLocked();

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

            // The cloud push runs OUTSIDE the lock. Holding the lock over a
            // network request (which can take up to the HTTP timeout) would
            // queue every cache reader behind it: on a cold start a single
            // background write could make settings, stats, daily rewards and
            // achievements time out, although each of them only needs a
            // memory read.
            //
            // The push can still be awaited (this method awaits it); only the
            // caller who chose to wait is affected.
            //
            // A batch provider is the exception: the write accumulates and goes
            // out in a batch a bit later. We can't wait for it here — callers
            // write keys one at a time and await each, so every batch would
            // hold a single key. Nothing can be lost: the key stays dirty in
            // PlayerPrefs until the provider confirms the write.
            if (_batch != null)
            {
                ScheduleBatchPush();
                return;
            }

            await PushDirtyKeysAsync();
        }

        public async Task<string> GetStringAsync(CloudSaveKey key)
        {
            ThrowIfDisposed();
            await _lock.WaitAsync();
            try
            {
                EnsureStateLoadedLocked();

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

                //Debug.Log($"{nameof(CloudSaveService)}: fetched '{keyStr}' — {(value != null ? "found" : "null")}");
                return value;
            }
            finally
            {
                _lock.Release();
            }
        }

        // Snapshot of everything the service considers the current state:
        // values plus their timestamps.
        //
        // Only reads memory under the shared lock — no writes, no provider
        // calls. That is why it can show the player the save's contents without
        // deciding anything for them: InitializeAsync, unlike this method, picks
        // a winner per key and immediately overwrites the loser.
        public async Task<IReadOnlyDictionary<string, CloudSaveEntry>> SnapshotAsync()
        {
            ThrowIfDisposed();
            await _lock.WaitAsync();
            try
            {
                EnsureStateLoadedLocked();

                var snapshot = new Dictionary<string, CloudSaveEntry>(_cache.Count);

                foreach (var pair in _cache)
                {
                    long ts = _timestamps.TryGetValue(pair.Key, out long known)
                        ? known
                        : GetLocalTimestamp(pair.Key);

                    snapshot[pair.Key] = new CloudSaveEntry(pair.Value, ts);
                }

                // Plus keys nobody has read this session: they exist only in
                // PlayerPrefs. The cache used to be enough — the cloud loaded
                // everything into it at startup. A guest save now lives only
                // locally with no cloud behind it, and without the index the
                // snapshot would see only what has been read so far, i.e.
                // almost nothing.
                foreach (var key in _knownKeys)
                {
                    if (snapshot.ContainsKey(key))
                        continue;

                    var value = _prefs.GetString(CloudKey(key), null);

                    if (value != null)
                        snapshot[key] = new CloudSaveEntry(value, GetLocalTimestamp(key));
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
                EnsureStateLoadedLocked();

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

            // The delete goes out in the same batch as writes — see SetStringAsync.
            if (_batch != null)
            {
                ScheduleBatchPush();
                return;
            }

            await PushDirtyKeysAsync();
        }

        // Push everything dirty right now and wait for the cloud to answer.
        //
        // Needed before a scene reload when the save owner changes: with a
        // batch provider, writes wait for their batch, and leaving the scene
        // without pushing them would leave them until the next launch — which,
        // having switched owners, clears the mirror together with them.
        public Task FlushPendingAsync()
        {
            ThrowIfDisposed();
            return PushDirtyKeysAsync();
        }

        public async Task DeleteAllAsync()
        {
            ThrowIfDisposed();

            await _lock.WaitAsync();
            try
            {
                EnsureStateLoadedLocked();
                ForgetLocalLocked();
                _prefs.Save();
            }
            finally
            {
                _lock.Release();
            }

            // Also outside the lock: deleting in the cloud is a network request too.
            await _provider.DeleteAllAsync();
        }

        // Forget the local mirror WITHOUT deleting anything in the cloud.
        //
        // Not to be confused with DeleteAllAsync, which erases the owner's save.
        // Here the cloud save stays untouched and only its local copy goes.
        // Needed on an identity switch: the guest mirror must not stay behind
        // under the account (or vice versa), otherwise the next push would write
        // someone else's values to the new owner.
        //
        // Afterwards the service counts as not initialized — the next
        // InitializeAsync loads everything again from the new identity's save.
        public async Task ResetLocalCacheAsync()
        {
            ThrowIfDisposed();

            await _lock.WaitAsync();
            try
            {
                EnsureStateLoadedLocked();
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

        // Shared part of DeleteAllAsync and ResetLocalCacheAsync. Call ONLY
        // under _lock.
        //
        // Clears the union of the cache and the key index: keys nobody has read
        // this session are missing from the cache but present in the index —
        // which is exactly why the index exists.
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

        // Push dirty keys to the cloud. The only place in the service that does
        // network I/O, and therefore the only one that must not hold _lock: while
        // a request is in flight (up to the HTTP client timeout) the cache must
        // stay readable.
        //
        // Instead, network work is guarded by its own _pushLock, which serialises
        // pushes without blocking readers, and state is read and updated in short
        // sections under _lock.
        private async Task PushDirtyKeysAsync()
        {
            // A push can outlive its caller: it is often started without being
            // awaited (a profile save, an achievement update) while the scene
            // reloads and the service is disposed. Returning to disposed
            // semaphores is not allowed — catch it here, otherwise every restart
            // logs an ObjectDisposedException although nothing is lost:
            // everything is already saved locally.
            try
            {
                await PushDirtyKeysCoreAsync();
            }
            catch (ObjectDisposedException)
            {
                Debug.Log($"{nameof(CloudSaveService)}: push aborted — the service is already disposed.");
            }
        }

        // Schedule a batch push unless one is already scheduled.
        //
        // Fire-and-forget on purpose: writes that arrive during the pause catch
        // up with the already scheduled push and go out with it. The pause is
        // measured with a Stopwatch and Task.Yield, not Task.Delay: WebGL has no
        // thread pool, and Task.Delay never wakes up there.
        private async void ScheduleBatchPush()
        {
            if (_batchPushScheduled || _disposed)
                return;

            _batchPushScheduled = true;

            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();

                while (watch.ElapsedMilliseconds < BatchDebounceMs)
                {
                    await Task.Yield();

                    if (_disposed)
                        return;
                }
            }
            finally
            {
                _batchPushScheduled = false;
            }

            try
            {
                await PushDirtyKeysAsync();
            }
            catch (Exception ex)
            {
                // async void: an exception escaping from here would crash the
                // app. The keys stay dirty and go out with the next push.
                Debug.LogWarning($"{nameof(CloudSaveService)}: batch push failed: {ex.Message}");
            }
        }

        private async Task PushDirtyKeysCoreAsync()
        {
            if (_disposed) return;

            if (_batch != null)
            {
                await PushDirtyKeysBatchAsync();
                return;
            }

            await _pushLock.WaitAsync();
            try
            {
                // Take a copy of the list: a new key may be added during the
                // push, and a set cannot be enumerated while it changes.
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

        // Batch push: all dirty keys in a few large requests instead of one
        // request per key.
        //
        // A key is cleared only if it hasn't been rewritten since the snapshot:
        // otherwise a new value written during the request would count as
        // delivered even though the cloud got the old one.
        private async Task PushDirtyKeysBatchAsync()
        {
            await _pushLock.WaitAsync();
            try
            {
                var entries = new Dictionary<string, CloudSaveEntry>();
                var deletes = new List<string>();

                await _lock.WaitAsync();
                try
                {
                    foreach (var key in _dirtyKeys)
                    {
                        if (_cache.TryGetValue(key, out var value))
                        {
                            long ts = _timestamps.TryGetValue(key, out var t) ? t : 0;
                            entries[key] = new CloudSaveEntry(value, ts);
                        }
                        else
                        {
                            deletes.Add(key);
                        }
                    }
                }
                finally
                {
                    _lock.Release();
                }

                if (entries.Count == 0 && deletes.Count == 0) return;

                Debug.Log($"{nameof(CloudSaveService)}: pushing {entries.Count} keys and {deletes.Count} deletes in batches...");

                int pushed = 0;

                foreach (var chunk in SplitIntoBatches(entries, deletes))
                {
                    try
                    {
                        await _batch.SetManyAsync(chunk.entries, chunk.deletes);
                    }
                    catch (Exception ex)
                    {
                        // Don't send the remaining batches: the network is most
                        // likely the same, and anything undelivered stays dirty
                        // and goes out next time.
                        Debug.LogWarning($"{nameof(CloudSaveService)}: batch push failed: {ex.Message}");
                        break;
                    }

                    await _lock.WaitAsync();
                    try
                    {
                        foreach (var pair in chunk.entries)
                        {
                            bool unchanged =
                                _cache.TryGetValue(pair.Key, out var current) &&
                                current == pair.Value.Value &&
                                _timestamps.TryGetValue(pair.Key, out var ts) &&
                                ts == pair.Value.TimestampUnixSeconds;

                            if (unchanged)
                                _dirtyKeys.Remove(pair.Key);
                        }

                        foreach (var key in chunk.deletes)
                        {
                            if (!_cache.ContainsKey(key))
                                _dirtyKeys.Remove(key);
                        }

                        SaveDirtyKeys();
                    }
                    finally
                    {
                        _lock.Release();
                    }

                    pushed += chunk.entries.Count + chunk.deletes.Count;
                }

                if (pushed > 0)
                    Debug.Log($"{nameof(CloudSaveService)}: pushed {pushed} keys");
            }
            finally
            {
                _pushLock.Release();
            }
        }

        // Split the push into batches so none hits the request body limit.
        // Deletes are tiny and ride along with the first batch.
        private static IEnumerable<(Dictionary<string, CloudSaveEntry> entries, List<string> deletes)> SplitIntoBatches(
            Dictionary<string, CloudSaveEntry> entries, List<string> deletes)
        {
            var current = new Dictionary<string, CloudSaveEntry>();
            var currentDeletes = deletes;
            long size = 0;

            foreach (var pair in entries)
            {
                long entrySize = pair.Key.Length + (pair.Value.Value?.Length ?? 0);

                if (current.Count > 0 && size + entrySize > MaxBatchChars)
                {
                    yield return (current, currentDeletes);

                    current = new Dictionary<string, CloudSaveEntry>();
                    currentDeletes = new List<string>();
                    size = 0;
                }

                current[pair.Key] = pair.Value;
                size += entrySize;
            }

            if (current.Count > 0 || currentDeletes.Count > 0)
                yield return (current, currentDeletes);
        }

        // Load the key index and the dirty set from PlayerPrefs. Once per
        // service lifetime, ONLY under _lock. See _stateLoaded.
        private void EnsureStateLoadedLocked()
        {
            if (_stateLoaded)
                return;

            LoadDirtyKeys();
            LoadKnownKeys();

            _stateLoaded = true;
        }

        // The envelope format lives in CloudSaveEnvelope: save summaries read it
        // too, and two separate parsers would eventually drift apart.
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

        // true: the key was not known before, so the index must be rewritten.
        //
        // Returns a "changed" flag instead of writing in place: stats and
        // achievements can have hundreds of keys, and rewriting the whole list
        // on every small save would be noticeable. New keys appear mostly
        // during warm-up.
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
