using System;
using UnityEngine;

namespace LongLeap.Localization
{
    /// <summary>
    /// Static convenience facade over <see cref="ILocalizationService"/>.
    /// Without a service attached it returns keys as-is, so callers never need
    /// a null check.
    /// </summary>
    public static class Loc
    {
        private static ILocalizationService _service;

        /// <summary>
        /// Raised when the active locale changes, and once on every
        /// <see cref="Init"/>.
        /// </summary>
        public static event Action OnLanguageChanged;

        /// <summary>
        /// True when a service is attached and the tables of the active locale
        /// are loaded. False before boot reaches localization and while a
        /// language switch is loading tables; during that time
        /// <see cref="Resolve"/> may return raw keys, so text should not be set
        /// from it yet.
        /// </summary>
        public static bool IsReady => _service != null && _service.IsReady;

        /// <summary>
        /// Attaches a localization service. Called once at boot; calling it again
        /// replaces the previous service. Raises <see cref="OnLanguageChanged"/>
        /// so components enabled before the service existed refresh their text.
        /// </summary>
        public static void Init(ILocalizationService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            if (_service != null)
                _service.OnLanguageChanged -= NotifyLanguageChanged;

            _service = service;
            _service.OnLanguageChanged += NotifyLanguageChanged;

            NotifyLanguageChanged();
        }

        /// <summary>
        /// Detaches <paramref name="service"/>. Call it together with disposing the
        /// service when its scene is torn down; otherwise the facade keeps a service
        /// whose tables are released and whose coroutine host is destroyed.
        /// Subscribers are not notified: they are being destroyed at the same time.
        /// </summary>
        /// <remarks>
        /// Does nothing if a different service is attached. A late Reset from an old
        /// scene must not detach the service of a scene that was already reloaded.
        /// </remarks>
        public static void Reset(ILocalizationService service)
        {
            if (service == null || _service != service)
                return;

            _service.OnLanguageChanged -= NotifyLanguageChanged;
            _service = null;
        }

        // Statics survive entering Play Mode with domain reload disabled; without
        // this, the next Play session would start with the previous session's service.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _service = null;
            OnLanguageChanged = null;
        }

        private static void NotifyLanguageChanged() => OnLanguageChanged?.Invoke();

        /// <summary>
        /// Resolves a key via the attached service. Without a service returns the
        /// key itself, or <see cref="string.Empty"/> for null.
        /// </summary>
        public static string Resolve(string key)
        {
            if (_service == null)
                return key ?? string.Empty;

            return _service.Resolve(key);
        }

        /// <summary>
        /// Resolves a key and formats it with <paramref name="args"/> via the attached
        /// service. Without a service formats the key itself; if the key is not a
        /// valid format string, returns it unformatted.
        /// </summary>
        public static string ResolveFormat(string key, params object[] args)
        {
            if (_service != null)
                return _service.ResolveFormat(key, args);

            // A stray brace in the key would make string.Format throw.
            try
            {
                return string.Format(Resolve(key), args);
            }
            catch (FormatException)
            {
                return Resolve(key);
            }
        }
    }
}
