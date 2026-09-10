using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LongLeap.Analytics
{
public readonly struct EventName : IEquatable<EventName>
{
    private static readonly Regex ValidPattern = new Regex(@"^[a-zA-Z][a-zA-Z0-9_]{0,39}$", RegexOptions.Compiled);

    public string Value { get; }

    public EventName(string name)
    {
        if (string.IsNullOrEmpty(name) || !ValidPattern.IsMatch(name))
        {
            Debug.LogError($"EventName: недопустимое имя события '{name}'. Используется 'invalid_event'.");
            Value = "invalid_event";
            return;
        }

        Value = name;
    }

    public bool Equals(EventName other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object obj) => obj is EventName other && Equals(other);

    public override int GetHashCode() => Value != null ? Value.GetHashCode() : 0;

    public static bool operator ==(EventName left, EventName right) => left.Equals(right);

    public static bool operator !=(EventName left, EventName right) => !left.Equals(right);

    public override string ToString() => Value ?? "invalid_event";
}
}
