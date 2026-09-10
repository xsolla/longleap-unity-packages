using System;
using System.Collections.Generic;
using System.Globalization;
using LongLeap.Core.CloudSaves;

namespace LongLeap.DevTools
{
    // Одна строка списка: ключ, значение как есть и уже отформатированная метка.
    public readonly struct CloudSaveRow
    {
        public string Key { get; }
        public string Value { get; }
        public string Timestamp { get; }

        public CloudSaveRow(string key, string value, string timestamp)
        {
            Key = key;
            Value = value;
            Timestamp = timestamp;
        }
    }

    // Группа строк с общим префиксом ключа.
    public readonly struct CloudSaveGroup
    {
        public string Name { get; }
        public IReadOnlyList<CloudSaveRow> Rows { get; }

        public CloudSaveGroup(string name, IReadOnlyList<CloudSaveRow> rows)
        {
            Name = name;
            Rows = rows;
        }
    }

    // Вся логика панели, вынесенная из MonoBehaviour, чтобы её можно было
    // проверить тестами. Ни одной ссылки на UnityEngine — как в SaveSummary и
    // XsollaIdAccountWidgetLogic.
    //
    // Группировка НАМЕРЕННО выведена из формы ключа, а не из знания о том, какие
    // ключи бывают у Blood Red Snow: пакет не должен знать ни одной игры. Разрез
    // по первому подчёркиванию даёт на нынешних ключах ровно stat / ach /
    // settings и (root) для одиночек — но останется осмысленным и на чужих.
    public static class CloudSaveDebugView
    {
        public const string RootGroupName = "(root)";
        public const string UnknownTimestamp = "unknown";
        public const string NullValue = "(null)";
        public const string EmptyValue = "(empty)";

        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

        public static IReadOnlyList<CloudSaveGroup> Build(
            IReadOnlyDictionary<string, CloudSaveEntry> snapshot, string filter)
        {
            var groups = new List<CloudSaveGroup>();
            if (snapshot == null || snapshot.Count == 0)
                return groups;

            var buckets = new Dictionary<string, List<CloudSaveRow>>(StringComparer.Ordinal);

            foreach (var pair in snapshot)
            {
                string key = pair.Key ?? string.Empty;
                if (!Matches(key, filter))
                    continue;

                string name = GroupOf(key);
                if (!buckets.TryGetValue(name, out var rows))
                {
                    rows = new List<CloudSaveRow>();
                    buckets[name] = rows;
                }

                rows.Add(new CloudSaveRow(
                    key,
                    pair.Value.Value,
                    FormatTimestamp(pair.Value.TimestampUnixSeconds)));
            }

            var names = new List<string>(buckets.Keys);
            names.Sort(StringComparer.Ordinal);

            // Порядок фиксированный: иначе список дёргался бы между кадрами,
            // потому что перечисление словаря порядок не гарантирует.
            foreach (string name in names)
            {
                var rows = buckets[name];
                rows.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                groups.Add(new CloudSaveGroup(name, rows));
            }

            return groups;
        }

        public static string GroupOf(string key)
        {
            if (string.IsNullOrEmpty(key))
                return RootGroupName;

            int split = key.IndexOf('_');

            // split == 0 — ключ начинается с подчёркивания, префикса нет.
            return split <= 0 ? RootGroupName : key.Substring(0, split);
        }

        // 0 в метке означает «время неизвестно»: так лежат значения, записанные
        // до появления конвертов. Показывать это как 1970 год нельзя — см.
        // комментарий у CloudSaveEntry.
        public static string FormatTimestamp(long unixSeconds)
        {
            if (unixSeconds <= 0)
                return UnknownTimestamp;

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                    .ToLocalTime()
                    .ToString(TimestampFormat, CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return UnknownTimestamp;
            }
        }

        // Значение для показа в строке. null и пустую строку различаем: в отладке
        // разница между «ключа нет значения» и «значение — пустая строка» важна.
        public static string Describe(string value, int max)
        {
            if (value == null)
                return NullValue;
            if (value.Length == 0)
                return EmptyValue;

            return Truncate(value, max);
        }

        public static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;
            if (max <= 0)
                return string.Empty;
            if (value.Length <= max)
                return value;

            return value.Substring(0, max) + "...";
        }

        private static bool Matches(string key, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;

            return key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
