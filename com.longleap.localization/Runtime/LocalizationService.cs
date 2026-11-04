using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace LongLeap.Localization
{
    /// <summary>
    /// <see cref="ILocalizationService"/> backed by Unity Localization string tables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keys are looked up in the tables in the order they were passed to the
    /// constructor; the first table with a non-empty entry wins. Only tables that
    /// are already loaded are searched; there is no lazy loading.
    /// </para>
    /// <para>
    /// On a locale change the tables of the new locale are loaded by a coroutine
    /// on the host set with <see cref="SetCoroutineHost"/>. Until they arrive the
    /// previous locale's strings stay in use and <see cref="IsReady"/> is false.
    /// </para>
    /// <para>
    /// Dispose the service (and call <see cref="Loc.Reset"/>) when its scene is
    /// torn down.
    /// </para>
    /// </remarks>
    public class LocalizationService : ILocalizationService, IDisposable
    {
        private readonly LocalizedStringTable[] _localizedTables;

        // Loaded tables, stored by index of _localizedTables rather than by name.
        // TableReference.TableCollectionName is unreliable at runtime for
        // GUID-based references, which is how the Inspector serializes them: in a
        // player build it returns a name only after the table's SharedTableData
        // has loaded, and null before that. In the Editor the name always comes
        // from the AssetDatabase, so name-keyed caching works there and silently
        // fails only in builds.
        private StringTable[] _tables;

        private MonoBehaviour _coroutineHost;

        // Incremented on every reload. The locale can be switched twice in a row
        // (e.g. the portal sets one, then player settings set another), leaving two
        // loads in flight; a load that finishes after a newer one has started must
        // not overwrite the newer result.
        private int _reloadGeneration;

        // Tables for a new locale are loading.
        private bool _reloading;

        // Keys already reported as missing for the current tables, so a key that is
        // resolved every frame logs once instead of flooding the console.
        private readonly HashSet<string> _reportedMissingKeys = new HashSet<string>();

        private bool _disposed;

        /// <inheritdoc />
        public event Action OnLanguageChanged;

        /// <inheritdoc />
        /// <remarks>
        /// The service is meant to be created after the initial table load (see
        /// <see cref="LoadTables"/>), so it starts ready and is only not ready while
        /// a locale change reloads tables. If the coroutine host is destroyed or
        /// deactivated mid-reload without <see cref="Dispose"/>, the reload is
        /// abandoned: the service reports ready again and keeps serving the
        /// previous locale's strings.
        /// </remarks>
        public bool IsReady => !_reloading || !CanRunCoroutines(_coroutineHost);

        /// <summary>
        /// Code of the selected locale (e.g. "en"), or null when there are no
        /// LocalizationSettings or the locale is still being selected.
        /// </summary>
        public string CurrentLocaleCode
        {
            get
            {
                if (!LocalizationSettings.HasSettings)
                    return null;

                // The async handle is read instead of SelectedLocale, whose getter
                // blocks until initialization completes; that is not supported on WebGL.
                var op = LocalizationSettings.SelectedLocaleAsync;
                if (!op.IsValid() || !op.IsDone || op.Result == null)
                    return null;

                return op.Result.Identifier.Code;
            }
        }

        /// <param name="localizedTables">Tables to search, in lookup priority order.</param>
        /// <param name="preloaded">
        /// Tables already loaded for the active locale, by the same indices as
        /// <paramref name="localizedTables"/>; usually filled by
        /// <see cref="LoadTables"/>. Null leaves every table unloaded until the
        /// next locale change.
        /// </param>
        public LocalizationService(LocalizedStringTable[] localizedTables,
            StringTable[] preloaded = null)
        {
            _localizedTables = localizedTables ?? Array.Empty<LocalizedStringTable>();
            _tables = new StringTable[_localizedTables.Length];

            if (preloaded != null)
            {
                int count = Mathf.Min(preloaded.Length, _tables.Length);
                for (int i = 0; i < count; i++)
                    _tables[i] = preloaded[i];
            }

            // LocalizationSettings.Instance throws a NullReferenceException when the
            // project has no settings asset; check first so the service can still be
            // created and resolve keys as-is.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnUnityLocaleChanged;
        }

        /// <summary>
        /// Sets the MonoBehaviour that runs table reloads on locale change.
        /// Without an active host a locale change drops all tables and
        /// <see cref="Resolve"/> returns raw keys.
        /// </summary>
        public void SetCoroutineHost(MonoBehaviour host)
        {
            _coroutineHost = host;
        }

        /// <summary>
        /// Unsubscribes from Unity Localization and abandons any reload in flight.
        /// Call it when the owning scene is torn down, together with
        /// <see cref="Loc.Reset"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnUnityLocaleChanged;

            // Loads in flight run on a host that is being destroyed with the scene;
            // mark them stale so none of them applies its result.
            _reloadGeneration++;
            _reloading = false;

            _coroutineHost = null;
        }

        /// <summary>Same as <see cref="Dispose"/>.</summary>
        [Obsolete("Use Dispose() instead.")]
        public void Shutdown() => Dispose();

        /// <summary>
        /// Codes of all locales available in LocalizationSettings. Empty when there are
        /// no settings or the locales are not loaded yet.
        /// </summary>
        public IReadOnlyList<string> GetAvailableLocaleCodes()
        {
            var codes = new List<string>();

            if (!LocalizationSettings.HasSettings)
                return codes;

            var locales = LocalizationSettings.AvailableLocales.Locales;
            for (int i = 0; i < locales.Count; i++)
            {
                if (locales[i] != null)
                    codes.Add(locales[i].Identifier.Code);
            }

            return codes;
        }

        private void OnUnityLocaleChanged(Locale locale)
        {
            // Raised both after our own SwitchLanguage (a frame later) and on external
            // changes, e.g. from the Unity Inspector. A second pass over the tables is
            // nearly free: GetTableAsync returns already-loaded tables from the
            // database cache.
            StartReload();
        }

        private static bool CanRunCoroutines(MonoBehaviour host)
            => host != null && host.gameObject.activeInHierarchy;

        // Reloads the tables for the current locale. IsReady is false until done.
        private void StartReload()
        {
            if (_disposed)
                return;

            _reloadGeneration++;

            // StartCoroutine does not run on an inactive object, so without a live,
            // active host the reload would never finish. The current tables hold the
            // previous locale and would show the wrong language, so drop them and say
            // so: texts falling back to keys must not happen silently.
            if (!CanRunCoroutines(_coroutineHost))
            {
                SetTables(new StringTable[_localizedTables.Length]);
                _reloading = false;

                Debug.LogWarning("[Localization] Locale changed, but there is no active " +
                    "coroutine host to load tables; texts will show raw keys.");

                OnLanguageChanged?.Invoke();
                return;
            }

            _reloading = true;

            _coroutineHost.StartCoroutine(ReloadTables(_reloadGeneration));
        }

        private IEnumerator ReloadTables(int generation)
        {
            // A locale change restarts Unity's initialization operation, which also
            // preloads the new locale's tables. Wait for it first, so "ready" means
            // everything has loaded, not only the tables requested here.
            if (LocalizationSettings.HasSettings)
            {
                var initOp = LocalizationSettings.InitializationOperation;

                if (initOp.IsValid() && !initOp.IsDone)
                    yield return initOp;
            }

            // Load into a separate array and swap it in at the end. Until the new
            // tables arrive (several frames or even seconds on WebGL), Resolve keeps
            // returning the previous translation instead of flashing raw keys.
            var loaded = new StringTable[_localizedTables.Length];

            yield return LoadTables(_localizedTables, loaded);

            // The locale was switched again while loading: this result is stale. The
            // reloading flag is left to the newer reload to clear.
            if (generation != _reloadGeneration)
                yield break;

            SetTables(loaded);
            _reloading = false;

            OnLanguageChanged?.Invoke();
        }

        private void SetTables(StringTable[] tables)
        {
            _tables = tables;
            _reportedMissingKeys.Clear();
        }

        /// <inheritdoc />
        /// <remarks>A missing key is logged once per key until the tables change.</remarks>
        public string Resolve(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            for (int i = 0; i < _tables.Length; i++)
            {
                var table = _tables[i];
                if (table == null)
                    continue;

                var entry = table.GetEntry(key);
                if (entry != null)
                {
                    var localized = entry.GetLocalizedString();
                    if (!string.IsNullOrEmpty(localized))
                        return localized;
                }
            }

            if (_reportedMissingKeys.Add(key))
                Debug.LogWarning($"[Localization] Missing translation for key '{key}'.");

            return key;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Uses <see cref="string.Format(string, object[])"/> placeholders
        /// (<c>{0}</c>); Unity Smart Strings arguments are not applied.
        /// </remarks>
        public string ResolveFormat(string key, params object[] args)
        {
            string resolved = Resolve(key);

            // Translations are data: a stray brace or fewer placeholders than
            // arguments must not throw mid-frame. Show the string as-is and log it.
            try
            {
                return string.Format(resolved, args);
            }
            catch (FormatException e)
            {
                Debug.LogWarning($"[Localization] Failed to format '{key}' " +
                    $"('{resolved}'): {e.Message}");

                return resolved;
            }
        }

        /// <summary>
        /// Switches the active locale and starts reloading tables.
        /// </summary>
        /// <returns>
        /// True if a switch started. False if the code is empty, there are no
        /// LocalizationSettings, the locale is not available, or it is already
        /// selected; in those cases no reload happens and the service stays ready.
        /// </returns>
        public bool SwitchLanguage(string localeCode)
        {
            if (string.IsNullOrEmpty(localeCode))
                return false;

            if (!LocalizationSettings.HasSettings)
            {
                Debug.LogWarning("[Localization] No LocalizationSettings; " +
                    $"cannot switch to locale '{localeCode}'.");
                return false;
            }

            var locales = LocalizationSettings.AvailableLocales.Locales;

            for (int i = 0; i < locales.Count; i++)
            {
                var locale = locales[i];
                if (locale == null || locale.Identifier.Code != localeCode)
                    continue;

                // Assigning the selected locale again neither reloads tables nor raises
                // an event; return false so the caller does not wait for a reload.
                if (LocalizationSettings.SelectedLocale == locale)
                    return false;

                LocalizationSettings.SelectedLocale = locale;

                // Start the reload now instead of waiting for SelectedLocaleChanged.
                // Unity raises that event from a coroutine once its re-created
                // initialization operation completes, i.e. at least a frame later
                // (see LocalizationSettings.SendLocaleChangedEvents). Until then
                // IsReady would still report true, and UI opened in that window
                // would show the previous language or raw keys.
                StartReload();

                return true;
            }

            Debug.LogWarning($"[Localization] Locale '{localeCode}' is not among the " +
                "available locales; locale not switched.");

            return false;
        }

        /// <summary>
        /// Loads the tables of the active locale into <paramref name="target"/>, by the
        /// same indices as in <paramref name="tables"/>. Entries that fail to load stay
        /// null and are logged as errors. Extra entries in the longer array are ignored.
        /// </summary>
        public static IEnumerator LoadTables(
            LocalizedStringTable[] tables,
            StringTable[] target)
        {
            if (tables == null || target == null)
                yield break;

            int count = Mathf.Min(tables.Length, target.Length);

            for (int i = 0; i < count; i++)
            {
                var table = tables[i];
                if (table == null)
                    continue;

                var op = table.GetTableAsync();
                yield return op;

                if (op.IsValid() && op.Result != null)
                {
                    target[i] = op.Result;
                }
                else
                {
                    // Identified by GUID: TableCollectionName is not reliably
                    // available at runtime (see the _tables comment).
                    Debug.LogError("[Localization] Failed to load table " +
                        $"#{i} (GUID {table.TableReference.TableCollectionNameGuid}).");
                }
            }
        }
    }
}
