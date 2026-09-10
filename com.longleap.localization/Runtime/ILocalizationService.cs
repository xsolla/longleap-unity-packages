using System;

namespace LongLeap.Localization
{
    /// <summary>
    /// Service that resolves localization keys into translated strings.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>
        /// Raised when the active locale changes.
        /// </summary>
        event Action OnLanguageChanged;

        /// <summary>
        /// True when the tables of the active locale are loaded and
        /// <see cref="Resolve"/> returns translations rather than raw keys.
        /// Goes false while a language switch is loading its tables.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Resolves a localization key to its translated value for the current locale.
        /// Returns the key itself if no translation is found.
        /// Returns <see cref="string.Empty"/> if the key is null or empty.
        /// </summary>
        string Resolve(string key);

        /// <summary>
        /// Resolves a key and formats the result with the supplied arguments
        /// via <see cref="string.Format(string, object[])"/>.
        /// </summary>
        string ResolveFormat(string key, params object[] args);
    }
}
