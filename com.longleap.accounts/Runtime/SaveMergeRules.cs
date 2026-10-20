using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LongLeap.Core.CloudSaves;

namespace LongLeap.Accounts
{
    // How one save key is merged when guest progress meets an account save.
    public enum SaveMergeRule
    {
        // The account's value; the guest's only if the account has none. For
        // state that can't be combined meaningfully: equipped items, settings,
        // calendars that would pay a reward twice.
        PreferAccount,

        // The guest's value; the account's only if the guest has none.
        PreferLocal,

        // The larger number. Never inflates progress: the result is a value one
        // of the two saves really had.
        Max,

        // The smaller positive number. Zero and negatives mean "unknown" (see
        // CloudSaveEntry) and are ignored — for "unlocked at" timestamps.
        Min,

        // A flag: set if it is set in either save ("1" or "true").
        Or,

        // A list of items joined by a separator: the union of both, account
        // items first, in their original order.
        Union
    }

    // Both saves, for custom rules that need to look at other keys — e.g. a
    // "today's score" value is only meaningful together with its day.
    public sealed class SaveMergeInput
    {
        public SaveMergeInput(
            IReadOnlyDictionary<string, CloudSaveEntry> local,
            IReadOnlyDictionary<string, CloudSaveEntry> account)
        {
            Local = local ?? new Dictionary<string, CloudSaveEntry>();
            Account = account ?? new Dictionary<string, CloudSaveEntry>();
        }

        public IReadOnlyDictionary<string, CloudSaveEntry> Local { get; }

        public IReadOnlyDictionary<string, CloudSaveEntry> Account { get; }

        public string LocalValue(string key) => Value(Local, key);

        public string AccountValue(string key) => Value(Account, key);

        private static string Value(IReadOnlyDictionary<string, CloudSaveEntry> source, string key) =>
            source.TryGetValue(key, out CloudSaveEntry entry) ? entry.Value : null;
    }

    // A custom rule: return the merged value, or null to drop the key.
    public delegate string SaveMergeFunc(string key, SaveMergeInput input);

    // A table of key patterns and how to merge each key.
    //
    // The table is the only game-specific part of merging: the save switch
    // knows nothing about coins or stats, it asks this table key by key.
    // Patterns are globs where '*' matches any run of characters
    // ("stat_*", "ach_*_unlocked"). The first matching pattern wins, so put
    // specific patterns before general ones. Keys no pattern matches use the
    // default rule.
    public sealed class SaveMergeRules
    {
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly SaveMergeRule _defaultRule;

        public SaveMergeRules(SaveMergeRule defaultRule = SaveMergeRule.PreferAccount)
        {
            _defaultRule = defaultRule;
        }

        public SaveMergeRules Add(string pattern, SaveMergeRule rule, char listSeparator = '\n')
        {
            _entries.Add(new Entry(pattern, rule, null, listSeparator));
            return this;
        }

        public SaveMergeRules Add(string pattern, SaveMergeFunc custom)
        {
            if (custom == null)
                throw new ArgumentNullException(nameof(custom));

            _entries.Add(new Entry(pattern, SaveMergeRule.PreferAccount, custom, '\n'));
            return this;
        }

        // Merge two saves. The result has every key of either save; a null
        // value means the key must not exist after the merge.
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

        public string MergeKey(string key, SaveMergeInput input)
        {
            Entry entry = Find(key);

            if (entry?.Custom != null)
                return entry.Custom(key, input);

            SaveMergeRule rule = entry?.Rule ?? _defaultRule;
            char separator = entry?.Separator ?? '\n';

            return Apply(rule, input.LocalValue(key), input.AccountValue(key), separator);
        }

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
            bool hasLocal = TryNumber(local, out double l) && (!ignoreNonPositive || l > 0);
            bool hasAccount = TryNumber(account, out double a) && (!ignoreNonPositive || a > 0);

            if (hasLocal && hasAccount)
            {
                bool takeLocal = preferLarger ? l > a : l < a;
                return takeLocal ? local : account;
            }

            if (hasAccount) return account;
            if (hasLocal) return local;

            return account ?? local;
        }

        private static bool TryNumber(string value, out double number)
        {
            number = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
            {
                number = whole;
                return true;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
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
