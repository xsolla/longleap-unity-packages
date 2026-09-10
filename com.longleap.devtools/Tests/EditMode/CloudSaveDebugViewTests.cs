using System;
using System.Collections.Generic;
using LongLeap.Core.CloudSaves;
using LongLeap.DevTools;
using NUnit.Framework;

namespace LongLeap.DevTools.Tests
{
    public class CloudSaveDebugViewTests
    {
        private static IReadOnlyDictionary<string, CloudSaveEntry> Snapshot(
            params (string key, string value, long ts)[] entries)
        {
            var dict = new Dictionary<string, CloudSaveEntry>();
            foreach (var (key, value, ts) in entries)
                dict[key] = new CloudSaveEntry(value, ts);

            return dict;
        }

        [Test]
        public void Build_NullSnapshot_ReturnsEmpty()
        {
            Assert.IsEmpty(CloudSaveDebugView.Build(null, null));
        }

        [Test]
        public void Build_EmptySnapshot_ReturnsEmpty()
        {
            Assert.IsEmpty(CloudSaveDebugView.Build(Snapshot(), null));
        }

        [Test]
        public void Build_GroupsByPrefixBeforeFirstUnderscore()
        {
            var groups = CloudSaveDebugView.Build(
                Snapshot(
                    ("stat_kill:monk", "3", 100),
                    ("stat_death", "1", 100),
                    ("ach_first_unlocked", "1", 100)),
                null);

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("ach", groups[0].Name);
            Assert.AreEqual(1, groups[0].Rows.Count);
            Assert.AreEqual("stat", groups[1].Name);
            Assert.AreEqual(2, groups[1].Rows.Count);
        }

        [Test]
        public void Build_KeyWithoutUnderscore_GoesToRootGroup()
        {
            var groups = CloudSaveDebugView.Build(
                Snapshot(("wallet", "1240", 100), ("heroes_owned", "[]", 100)),
                null);

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(CloudSaveDebugView.RootGroupName, groups[0].Name);
            Assert.AreEqual("wallet", groups[0].Rows[0].Key);
            Assert.AreEqual("heroes", groups[1].Name);
        }

        [Test]
        public void GroupOf_LeadingUnderscore_IsRoot()
        {
            Assert.AreEqual(CloudSaveDebugView.RootGroupName, CloudSaveDebugView.GroupOf("_version"));
        }

        [Test]
        public void GroupOf_NullOrEmpty_IsRoot()
        {
            Assert.AreEqual(CloudSaveDebugView.RootGroupName, CloudSaveDebugView.GroupOf(null));
            Assert.AreEqual(CloudSaveDebugView.RootGroupName, CloudSaveDebugView.GroupOf(string.Empty));
        }

        [Test]
        public void Build_SortsGroupsAndRowsOrdinally()
        {
            var groups = CloudSaveDebugView.Build(
                Snapshot(
                    ("stat_z", "1", 100),
                    ("stat_a", "1", 100),
                    ("ach_x", "1", 100)),
                null);

            Assert.AreEqual(new[] { "ach", "stat" }, new[] { groups[0].Name, groups[1].Name });
            Assert.AreEqual("stat_a", groups[1].Rows[0].Key);
            Assert.AreEqual("stat_z", groups[1].Rows[1].Key);
        }

        [Test]
        public void Build_FilterMatchesKeyCaseInsensitively()
        {
            var groups = CloudSaveDebugView.Build(
                Snapshot(("stat_kill:monk", "3", 100), ("wallet", "1240", 100)),
                "STAT_");

            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual("stat", groups[0].Name);
            Assert.AreEqual("stat_kill:monk", groups[0].Rows[0].Key);
        }

        [Test]
        public void Build_FilterOmitsGroupsThatEndUpEmpty()
        {
            var groups = CloudSaveDebugView.Build(
                Snapshot(("stat_kill", "3", 100), ("ach_x_ts", "1", 100)),
                "zzz");

            Assert.IsEmpty(groups);
        }

        [Test]
        public void Build_EmptyFilterMatchesEverything()
        {
            var groups = CloudSaveDebugView.Build(
                Snapshot(("stat_kill", "3", 100), ("wallet", "1", 100)),
                string.Empty);

            Assert.AreEqual(2, groups.Count);
        }

        [Test]
        public void Build_CarriesRawValueAndFormattedTimestamp()
        {
            var groups = CloudSaveDebugView.Build(Snapshot(("wallet", "1240", 0)), null);
            CloudSaveRow row = groups[0].Rows[0];

            Assert.AreEqual("1240", row.Value);
            Assert.AreEqual(CloudSaveDebugView.UnknownTimestamp, row.Timestamp);
        }

        // Метка 0 означает «время неизвестно» — показывать её как 1970 год
        // запрещено комментарием у CloudSaveEntry.
        [Test]
        public void FormatTimestamp_Zero_IsUnknownNotEpoch()
        {
            Assert.AreEqual(CloudSaveDebugView.UnknownTimestamp, CloudSaveDebugView.FormatTimestamp(0));
        }

        [Test]
        public void FormatTimestamp_Negative_IsUnknown()
        {
            Assert.AreEqual(CloudSaveDebugView.UnknownTimestamp, CloudSaveDebugView.FormatTimestamp(-1));
        }

        [Test]
        public void FormatTimestamp_OutOfRange_IsUnknown()
        {
            Assert.AreEqual(CloudSaveDebugView.UnknownTimestamp, CloudSaveDebugView.FormatTimestamp(long.MaxValue));
        }

        [Test]
        public void FormatTimestamp_RealStamp_RendersAsLocalDate()
        {
            const long stamp = 1756000000L;
            string expected = DateTimeOffset.FromUnixTimeSeconds(stamp)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            Assert.AreEqual(expected, CloudSaveDebugView.FormatTimestamp(stamp));
        }

        [Test]
        public void Truncate_ShorterThanMax_IsUnchanged()
        {
            Assert.AreEqual("abc", CloudSaveDebugView.Truncate("abc", 10));
        }

        [Test]
        public void Truncate_LongerThanMax_IsCutAndMarked()
        {
            Assert.AreEqual("abc...", CloudSaveDebugView.Truncate("abcdef", 3));
        }

        [Test]
        public void Truncate_NullOrEmpty_DoesNotThrow()
        {
            Assert.AreEqual(string.Empty, CloudSaveDebugView.Truncate(null, 10));
            Assert.AreEqual(string.Empty, CloudSaveDebugView.Truncate(string.Empty, 10));
        }

        [Test]
        public void Describe_DistinguishesNullFromEmpty()
        {
            Assert.AreEqual(CloudSaveDebugView.NullValue, CloudSaveDebugView.Describe(null, 10));
            Assert.AreEqual(CloudSaveDebugView.EmptyValue, CloudSaveDebugView.Describe(string.Empty, 10));
        }

        [Test]
        public void Describe_LongValue_IsTruncated()
        {
            Assert.AreEqual("abc...", CloudSaveDebugView.Describe("abcdef", 3));
        }
    }
}
