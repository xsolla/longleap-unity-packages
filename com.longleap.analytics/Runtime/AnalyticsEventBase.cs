using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Analytics
{
    // Base class for typed game events:
    //
    //     public sealed class LevelCompleted : AnalyticsEventBase
    //     {
    //         private static readonly EventName EventId = new EventName("level_completed");
    //
    //         public LevelCompleted(int level, double seconds) : base(EventId)
    //         {
    //             SetParam("level", level);
    //             SetParam("duration_sec", seconds);
    //         }
    //     }
    //
    // SetParam validates against GA4 rules immediately, so mistakes show up in
    // the console where the event is built, not as missing data in reports.
    // Rejected params are logged as warnings and skipped; they never throw.
    public abstract class AnalyticsEventBase : IAnalyticsEvent
    {
        private readonly Dictionary<string, object> _params = new Dictionary<string, object>();

        protected AnalyticsEventBase(EventName name)
        {
            Name = name;
        }

        public EventName Name { get; }

        public IReadOnlyDictionary<string, object> Params => _params;

        // A null string removes the parameter.
        protected void SetParam(string key, string value)
        {
            if (value == null)
            {
                RemoveParam(key);
                return;
            }

            Store(key, value);
        }

        // int, uint, short, ushort, byte and sbyte bind here.
        protected void SetParam(string key, long value) => Store(key, value);

        // float binds here. NaN and infinities are rejected. Note that ulong
        // binds here too and loses precision above 2^53: cast it to long.
        protected void SetParam(string key, double value) => Store(key, value);

        // Sent as "true" / "false": GA4 has no boolean parameter type.
        protected void SetParam(string key, bool value) => Store(key, value);

        protected bool RemoveParam(string key) => key != null && _params.Remove(key);

        private void Store(string key, object value)
        {
            if (!Ga4Rules.IsValidParamName(key, out var reason))
            {
                Debug.LogWarning($"{GetType().Name} '{Name}': parameter '{key}' skipped: {reason}.");
                return;
            }

            if (Ga4Rules.IsServiceParam(key))
            {
                Debug.LogWarning($"{GetType().Name} '{Name}': parameter '{key}' skipped: it is set by AnalyticsService.");
                return;
            }

            if (!Ga4Rules.TryNormalizeParamValue(value, out var normalized, out reason))
            {
                Debug.LogWarning($"{GetType().Name} '{Name}': parameter '{key}' skipped: {reason}.");
                return;
            }

            if (reason != null)
                Debug.LogWarning($"{GetType().Name} '{Name}': parameter '{key}': {reason}.");

            if (!_params.ContainsKey(key) && _params.Count >= Ga4Rules.MaxCustomParams)
            {
                Debug.LogWarning(
                    $"{GetType().Name} '{Name}': parameter '{key}' skipped: an event may carry at most " +
                    $"{Ga4Rules.MaxCustomParams} parameters ({Ga4Rules.ReservedParamSlots} more are reserved for the service).");
                return;
            }

            _params[key] = normalized;
        }
    }
}
