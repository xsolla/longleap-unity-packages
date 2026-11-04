using System;
using UnityEngine;

namespace LongLeap.Analytics
{
    // A GA4-valid event name: starts with a letter, letters/digits/underscores
    // only, at most 40 characters, no reserved prefix (google_, ga_, firebase_)
    // and not a reserved event name (session_start, first_visit, ...).
    //
    // Invalid input does not throw: analytics must never break gameplay. The
    // constructor logs an error and yields None; AnalyticsService drops events
    // whose name is None. default(EventName) is None as well.
    public readonly struct EventName : IEquatable<EventName>
    {
        public static readonly EventName None = default;

        private readonly string _value;

        public EventName(string name)
        {
            if (!Ga4Rules.IsValidEventName(name, out var reason))
            {
                Debug.LogError($"EventName: '{name}' is not a valid GA4 event name: {reason}. Events with this name are dropped.");
                _value = null;
                return;
            }

            _value = name;
        }

        private EventName(string validatedName, bool _)
        {
            _value = validatedName;
        }

        // The name, or an empty string for None.
        public string Value => _value ?? string.Empty;

        public bool IsValid => _value != null;

        // Same check as the constructor, without logging.
        public static bool TryCreate(string name, out EventName eventName)
        {
            if (Ga4Rules.IsValidEventName(name, out _))
            {
                eventName = new EventName(name, true);
                return true;
            }

            eventName = None;
            return false;
        }

        public bool Equals(EventName other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is EventName other && Equals(other);

        public override int GetHashCode() => _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public static bool operator ==(EventName left, EventName right) => left.Equals(right);

        public static bool operator !=(EventName left, EventName right) => !left.Equals(right);

        public override string ToString() => _value ?? "<none>";
    }
}
