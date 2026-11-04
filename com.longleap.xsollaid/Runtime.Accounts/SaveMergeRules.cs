using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LongLeap.Core.CloudSaves;

namespace LongLeap.XsollaId.Accounts
{
    /// <summary>
    /// How one save key is merged when guest progress meets an account save.
    /// </summary>
    public enum SaveMergeRule
    {
        /// <summary>
        /// The account's value; the guest's only if the account has none. For
        /// state that can't be combined meaningfully: equipped items, settings,
        /// calendars that would pay a reward twice.
        /// </summary>
        PreferAccount,

        /// <summary>The guest's value; the account's only if the guest has none.</summary>
        PreferLocal,

        /// <summary>
        /// The larger number. Never inflates progress: the result is a value one
        /// of the two saves really had.
        /// </summary>
        Max,

        /// <summary>
        /// The smaller positive number. Zero and negatives mean "unknown" (see
        /// <see cref="CloudSaveEntry"/>) and are ignored — for "unlocked at"
        /// timestamps.
        /// </summary>
        Min,

        /// <summary>A flag: set if it is set in either save (<c>"1"</c> or <c>"true"</c>).</summary>
        Or,

        /// <summary>
        /// A list of items joined by a separator: the union of both, account
        /// items first, in their original order.
        /// </summary>
        Union
    }

    /// <summary>
    /// Both saves, for custom rules that need to look at other keys — e.g. a
    /// "today's score" value is only meaningful together with its day.
    /// </summary>
    public sealed class SaveMergeInput
    {
        /// <param name="local">The guest save; <c>null</c> is treated as empty.</param>
        /// <param name="account">The account save; <c>null</c> is treated as empty.</param>
        public SaveMergeInput(
            IReadOnlyDictionary<string, CloudSaveEntry> local,
            IReadOnlyDictionary<string, CloudSaveEntry> account)
        {
            Local = local ?? new Dictionary<string, CloudSaveEntry>();
            Account = account ?? new Dictionary<string, CloudSaveEntry>();
        }

        /// <summary>The guest save (the local mirror).</summary>
        public IReadOnlyDictionary<string, CloudSaveEntry> Local { get; }

        /// <summary>The account save as read from the server.</summary>
        public IReadOnlyDictionary<string, CloudSaveEntry> Account { get; }

        /// <summary>The guest's value of <paramref name="key"/>, or <c>null</c>.</summary>
        public string LocalValue(string key) => Value(Local, key);

        /// <summary>The account's value of <paramref name="key"/>, or <c>null</c>.</summary>
        public string AccountValue(string key) => Value(Account, key);

        private static string Value(IReadOnlyDictionary<string, CloudSaveEntry> source, string key) =>
            source.TryGetValue(key, out CloudSaveEntry entry) ? entry.Value : null;
    }

    /// <summary>
    /// A custom rule: return the merged value, or <c>null</c> to drop the key.
    /// </summary>
    public delegate string SaveMergeFunc(string key, SaveMergeInput input);

    /// <summary>
    /// A table of key patterns and how to merge each key.
    /// </summary>
    /// <remarks>
    /// The table is the only game-specific part of merging: the save switch
    /// knows nothing about coins or stats, it asks this table key by key.
    /// Patterns are globs where <c>*</c> matches any run of characters
    /// (<c>"stat_*"</c>, <c>"ach_*_unlocked"</c>); no other wildcards, and
    /// matching is case-sensitive. The first matching pattern wins, so put
    /// specific patterns before general ones. Keys no pattern matches use the
    /// default rule.
    /// </remarks>
    public sealed class SaveMergeRules
    {
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly SaveMergeRule _defaultRule;

        /// <param name="defaultRule">The rule for keys no pattern matches.</param>
        public SaveMergeRules(SaveMergeRule defaultRule = SaveMergeRule.PreferAccount)
        {
            _defaultRule = defaultRule;
        }

        /// <summary>Merge keys matching <paramref name="pattern"/> by a built-in rule.</summary>
        /// <param name="listSeparator">The item separator for <see cref="SaveMergeRule.Union"/>.</param>
        public SaveMergeRules Add(string pattern, SaveMergeRule rule, char listSeparator = '\n')
        {
            _entries.Add(new Entry(pattern, rule, null, listSeparator));
            return this;
        }

        /// <summary>Merge keys matching <paramref name="pattern"/> by a custom function.</summary>
        public SaveMergeRules Add(string pattern, SaveMergeFunc custom)
        {
            if (custom == null)
                throw new ArgumentNullException(nameof(custom));

            _entries.Add(new Entry(pattern, SaveMergeRule.PreferAccount, custom, '\n'));
            return this;
        }

        /// <summary>
        /// Merge two saves. The result has every key of either save; a
        /// <c>null</c> value means the key must not exist after the merge.
        /// </summary>
        public Dictionary<string, string> Merge(
            IReadOnlyDictionary<string, CloudSaveEntry> local,
            IReadOnlyDictionary<string, CloudSaveEntry> account)
        {
            var input = new SaveMergeInput(local, account);
            var keys = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var pair in input.Local)
                keys.Add(pair.Key);

            foreach (var pair in input.Account)
                keys.Add(pair.Key);

            var result = new Dictionary<string, string>(keys.Count, StringComparer.Ordinal);

            foreach (string key in keys)
                result[key] = MergeKey(key, input);

            return result;
        }

        /// <summary>Merge one key by the first matching pattern, or the default rule.</summary>
        public string MergeKey(string key, SaveMergeInput input)
        {
            Entry entry = Find(key);

            if (entry?.Custom != null)
                return entry.Custom(key, input);

            SaveMergeRule rule = entry?.Rule ?? _defaultRule;
            char separator = entry?.Separator ?? '\n';

            return Apply(rule, input.LocalValue(key), input.AccountValue(key), separator);
        }

        /// <summary>
        /// Apply a built-in rule to two raw values. Useful inside custom rules.
        /// Numbers are parsed with the invariant culture.
        /// </summary>
        /// <param name="local">The guest's value, or <c>null</c>.</param>
        /// <param name="account">The account's value, or <c>null</c>.</param>
        public static string Apply(SaveMergeRule rule, string local, string account, char listSeparator = '\n')
        {
            switch (rule)
            {
                case SaveMergeRule.PreferLocal:
                    return local ?? account;

                case SaveMergeRule.Max:
                    return PickNumber(local, account, preferLarger: true, ignoreNonPositive: false);

                case SaveMergeRule.Min:
                    return PickNumber(local, account, preferLarger: false, ignoreNonPositive: true);

                case SaveMergeRule.Or:
                    if (IsSet(account)) return account;
                    if (IsSet(local)) return local;
                    return account ?? local;

                case SaveMergeRule.Union:
                    return Union(local, account, listSeparator);

                default:
                    return account ?? local;
            }
        }

        private Entry Find(string key)
        {
            foreach (Entry entry in _entries)
            {
                if (entry.Matches(key))
                    return entry;
            }

            return null;
        }

        // Pick one of the two values by number. An unparsable value counts as
        // missing; if neither parses, the account's raw value is kept so a
        // format we don't understand is not destroyed.
        private static string PickNumber(string local, string account, bool preferLarger, bool ignoreNonPositive)
        {
            bool hasLocal = TryNumber(local, out double l, out long? lWhole) && (!ignoreNonPositive || l > 0);
            bool hasAccount = TryNumber(account, out double a, out long? aWhole) && (!ignoreNonPositive || a > 0);

            if (hasLocal && hasAccount)
            {
                // Two integers are compared exactly: a double holds only 53 bits,
                // and large ids or millisecond timestamps would compare as equal.
                int order = lWhole.HasValue && aWhole.HasValue
                    ? lWhole.Value.CompareTo(aWhole.Value)
                    : l.CompareTo(a);

                bool takeLocal = preferLarger ? order > 0 : order < 0;
                return takeLocal ? local : account;
            }

            if (hasAccount) return account;
            if (hasLocal) return local;

            return account ?? local;
        }

        // whole — the exact value when the string is an integer.
        private static bool TryNumber(string value, out double number, out long? whole)
        {
            number = 0;
            whole = null;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                whole = parsed;
                number = parsed;
                return true;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !double.IsNaN(number);
        }

        private static bool IsSet(string value) =>
            value != null &&
            (value.Trim() == "1" || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

        private static string Union(string local, string account, char separator)
        {
            if (local == null && account == null)
                return null;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var builder = new StringBuilder();

            void AddAll(string list)
            {
                if (string.IsNullOrEmpty(list))
                    return;

                foreach (string item in list.Split(separator))
                {
                    if (string.IsNullOrEmpty(item) || !seen.Add(item))
                        continue;

                    if (builder.Length > 0)
                        builder.Append(separator);

                    builder.Append(item);
                }
            }

            AddAll(account);
            AddAll(local);

            return builder.ToString();
        }

        private sealed class Entry
        {
            private readonly Regex _pattern;

            public Entry(string pattern, SaveMergeRule rule, SaveMergeFunc custom, char separator)
            {
                if (string.IsNullOrEmpty(pattern))
                    throw new ArgumentException("SaveMergeRules: the key pattern is empty.", nameof(pattern));

                _pattern = new Regex(
                    "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                    RegexOptions.CultureInvariant);

                Rule = rule;
                Custom = custom;
                Separator = separator;
            }

            public SaveMergeRule Rule { get; }

            public SaveMergeFunc Custom { get; }

            public char Separator { get; }

            public bool Matches(string key) => _pattern.IsMatch(key);
        }
    }
}
