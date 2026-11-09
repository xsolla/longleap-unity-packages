using System.Globalization;

namespace LongLeap.BuildManager.Editor
{
    // The game version as stored in PlayerSettings.bundleVersion and read at
    // runtime through Application.version.
    //
    // A version is 1 to 4 dot-separated non-negative whole numbers: "1",
    // "1.0", "1.0.2", "1.0.2.7". Versions compare part by part as numbers,
    // not as text, so 1.0.10 is higher than 1.0.9. A missing part counts as
    // zero, so 1.0 and 1.0.0 are equal.
    internal static class BundleVersion
    {
        public const int MaxParts = 4;

        // Suggested when the current version cannot be parsed, so there is
        // nothing to increment.
        public const string Fallback = "1.0.0";

        public static bool TryParse(string text, out int[] parts)
        {
            parts = null;

            if (string.IsNullOrEmpty(text))
                return false;

            string[] tokens = text.Split('.');

            if (tokens.Length > MaxParts)
                return false;

            var result = new int[tokens.Length];

            for (int i = 0; i < tokens.Length; i++)
            {
                // NumberStyles.None: digits only — no sign, no whitespace, no
                // thousands separators.
                if (!int.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
                    return false;
            }

            parts = result;
            return true;
        }

        public static int Compare(int[] a, int[] b)
        {
            int length = a.Length > b.Length ? a.Length : b.Length;

            for (int i = 0; i < length; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;

                if (x != y)
                    return x < y ? -1 : 1;
            }

            return 0;
        }

        // The next version: the last part raised by one, the rest kept as is
        // (1.0.1 -> 1.0.2).
        public static string Increment(string version)
        {
            if (!TryParse(version, out int[] parts))
                return Fallback;

            int last = parts.Length - 1;

            // Practically unreachable, but an overflow would wrap to a
            // negative number and produce an invalid version.
            if (parts[last] == int.MaxValue)
                return Fallback;

            parts[last]++;

            var text = new string[parts.Length];

            for (int i = 0; i < parts.Length; i++)
                text[i] = parts[i].ToString(CultureInfo.InvariantCulture);

            return string.Join(".", text);
        }

        // Why the new version cannot be used, or null when it can.
        public static string Validate(string current, string next)
        {
            if (!TryParse(next, out int[] nextParts))
            {
                return $"New version '{next}' is not valid. Use 1 to {MaxParts} dot-separated " +
                       "whole numbers, for example 1.0.2.";
            }

            // An unparseable current version cannot be compared against. Any
            // valid new version replaces it.
            if (!TryParse(current, out int[] currentParts))
                return null;

            if (Compare(nextParts, currentParts) <= 0)
                return $"New version {next} must be higher than the current {current}.";

            return null;
        }
    }
}
