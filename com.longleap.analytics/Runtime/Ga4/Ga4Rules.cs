using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LongLeap.Analytics
{
    // GA4 Measurement Protocol limits and naming rules, kept in one place so the
    // event model, the service and every backend enforce exactly the same thing.
    // Source: https://developers.google.com/analytics/devguides/collection/protocol/ga4/sending-events#limitations
    //
    // Anything that breaks these rules is silently discarded by GA4 (the
    // collect endpoint answers 2xx regardless), so the package checks them on
    // the client and logs the reason instead.
    public static class Ga4Rules
    {
        public const int MaxEventsPerRequest = 25;
        public const int MaxParamsPerEvent = 25;
        public const int MaxNameLength = 40;
        public const int MaxParamValueLength = 100;
        public const int MaxUserProperties = 25;
        public const int MaxUserPropertyNameLength = 24;
        public const int MaxUserPropertyValueLength = 36;
        public const int MaxUserIdLength = 256;

        // GA4 refuses request bodies of 130 kB or more.
        public const int MaxRequestBytes = 130_000;

        // GA4 accepts events backdated by at most 72 hours.
        public static readonly TimeSpan MaxEventAge = TimeSpan.FromHours(72);

        // Parameter slots kept free for the service: session_id,
        // engagement_time_msec and up to three experiment tags. A game event
        // may use the rest, so the service never has to drop its own params.
        public const int ReservedParamSlots = 5;
        public const int MaxCustomParams = MaxParamsPerEvent - ReservedParamSlots;

        // Params the service owns and stamps on every event. A game event that
        // sets them is rejected, because the service would overwrite them.
        public const string SessionIdParam = "session_id";
        public const string EngagementTimeParam = "engagement_time_msec";

        private static readonly Regex NamePattern =
            new Regex("^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        private static readonly string[] ReservedPrefixes = { "google_", "ga_", "firebase_" };

        private static readonly HashSet<string> ReservedEventNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ad_activeview", "ad_click", "ad_exposure", "ad_query", "ad_reward", "adunit_exposure",
            "app_background", "app_clear_data", "app_exception", "app_remove", "app_store_refund",
            "app_store_subscription_cancel", "app_store_subscription_convert",
            "app_store_subscription_renew", "app_install", "app_update", "app_upgrade",
            "dynamic_link_app_open", "dynamic_link_app_update", "dynamic_link_first_open",
            "error", "first_open", "first_visit", "in_app_purchase",
            "notification_dismiss", "notification_foreground", "notification_open",
            "notification_receive", "notification_send", "os_update", "session_start", "session_start_with_rollout",
            "user_engagement",
        };

        private static readonly HashSet<string> ReservedUserPropertyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "first_open_time", "first_visit_time", "last_deep_link_referrer", "user_id",
            "first_open_after_install",
        };

        public static bool IsValidEventName(string name, out string reason)
        {
            if (!IsValidName(name, MaxNameLength, out reason))
                return false;

            if (ReservedEventNames.Contains(name))
            {
                reason = "the name is reserved by GA4";
                return false;
            }

            return true;
        }

        public static bool IsValidParamName(string name, out string reason) =>
            IsValidName(name, MaxNameLength, out reason);

        public static bool IsValidUserPropertyName(string name, out string reason)
        {
            if (!IsValidName(name, MaxUserPropertyNameLength, out reason))
                return false;

            if (ReservedUserPropertyNames.Contains(name))
            {
                reason = "the name is reserved by GA4";
                return false;
            }

            return true;
        }

        public static bool IsServiceParam(string name) =>
            name == SessionIdParam || name == EngagementTimeParam;

        // Cuts a string to maxLength UTF-16 units without splitting a
        // surrogate pair (an emoji cut in half becomes U+FFFD in the payload).
        public static string Truncate(string value, int maxLength)
        {
            if (value == null || value.Length <= maxLength)
                return value;

            var cut = maxLength;
            if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
                cut--;

            return value.Substring(0, cut);
        }

        // Converts a parameter value to one of the three types GA4 accepts:
        // string (cut to MaxParamValueLength), long or double. bool becomes
        // "true"/"false" because GA4 has no boolean type. Returns false for
        // null, NaN, infinities, ulong values above long.MaxValue and any
        // other type (nested objects, arrays).
        public static bool TryNormalizeParamValue(object value, out object normalized, out string reason)
        {
            reason = null;
            normalized = null;

            switch (value)
            {
                case null:
                    reason = "the value is null";
                    return false;
                case string s:
                    if (s.Length > MaxParamValueLength)
                    {
                        reason = $"the value was cut to {MaxParamValueLength} characters";
                        s = Truncate(s, MaxParamValueLength);
                    }

                    normalized = s;
                    return true;
                case bool b:
                    normalized = b ? "true" : "false";
                    return true;
                case int i:
                    normalized = (long)i;
                    return true;
                case long l:
                    normalized = l;
                    return true;
                case short sh:
                    normalized = (long)sh;
                    return true;
                case ushort us:
                    normalized = (long)us;
                    return true;
                case byte by:
                    normalized = (long)by;
                    return true;
                case sbyte sb:
                    normalized = (long)sb;
                    return true;
                case uint ui:
                    normalized = (long)ui;
                    return true;
                case ulong ul:
                    if (ul > long.MaxValue)
                    {
                        reason = "the value is larger than long.MaxValue";
                        return false;
                    }

                    normalized = (long)ul;
                    return true;
                case float f:
                    return TryNormalizeDouble(f, out normalized, out reason);
                case double d:
                    return TryNormalizeDouble(d, out normalized, out reason);
                case decimal m:
                    return TryNormalizeDouble((double)m, out normalized, out reason);
                default:
                    reason = $"type {value.GetType().Name} is not supported (use string, integer, floating point or bool)";
                    return false;
            }
        }

        private static bool TryNormalizeDouble(double value, out object normalized, out string reason)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                normalized = null;
                reason = "the value is NaN or infinity";
                return false;
            }

            normalized = value;
            reason = null;
            return true;
        }

        private static bool IsValidName(string name, int maxLength, out string reason)
        {
            if (string.IsNullOrEmpty(name))
            {
                reason = "the name is empty";
                return false;
            }

            if (name.Length > maxLength)
            {
                reason = $"the name is longer than {maxLength} characters";
                return false;
            }

            if (!NamePattern.IsMatch(name))
            {
                reason = "the name must start with a letter and contain only letters, digits and underscores";
                return false;
            }

            foreach (var prefix in ReservedPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    reason = $"the prefix '{prefix}' is reserved by GA4";
                    return false;
                }
            }

            reason = null;
            return true;
        }
    }
}
