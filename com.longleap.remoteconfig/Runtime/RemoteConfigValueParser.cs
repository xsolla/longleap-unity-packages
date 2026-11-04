using System;
using System.Globalization;
using Newtonsoft.Json;

namespace LongLeap.RemoteConfig
{
    // Parses raw remote config strings. Rules are documented on IRemoteConfigService.
    internal static class RemoteConfigValueParser
    {
        public static bool TryParseInt(string raw, out int value)
        {
            if (TryParseLong(raw, out var wide) && wide >= int.MinValue && wide <= int.MaxValue)
            {
                value = (int)wide;
                return true;
            }

            value = 0;
            return false;
        }

        public static bool TryParseLong(string raw, out long value)
        {
            value = 0;
            if (raw == null)
                return false;

            var text = raw.Trim();
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            // "42.0" or "1e3": whole numbers written as decimals. The upper bound is exclusive
            // because (double)long.MaxValue rounds up to 2^63.
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                && Math.Floor(d) == d
                && d >= long.MinValue && d < long.MaxValue)
            {
                value = (long)d;
                return true;
            }

            value = 0;
            return false;
        }

        public static bool TryParseDouble(string raw, out double value)
        {
            value = 0;
            return raw != null
                && double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static bool TryParseFloat(string raw, out float value)
        {
            value = 0f;
            return raw != null
                && float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryParseBool(string raw, out bool value)
        {
            value = false;
            if (raw == null)
                return false;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "true": case "t": case "yes": case "y": case "on": case "1":
                    value = true;
                    return true;
                case "false": case "f": case "no": case "n": case "off": case "0":
                    value = false;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryParseJson<T>(string raw, out T value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            try
            {
                value = JsonConvert.DeserializeObject<T>(raw);
                // "null" deserializes to null for reference types; a getter must not hand that
                // out instead of the caller's default.
                return value != null;
            }
            catch (JsonException)
            {
                value = default;
                return false;
            }
        }
    }
}
