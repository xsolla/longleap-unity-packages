using System;
using UnityEngine;

namespace LongLeap.Localization
{
    /// <summary>
    /// Serializable localization key. Use it for Inspector fields instead of a
    /// bare <see cref="string"/> so keys are recognizable in configs and code.
    /// </summary>
    /// <remarks>
    /// Converts implicitly to and from <see cref="string"/>. Null and empty
    /// strings both produce an empty key (<see cref="IsEmpty"/>), and all empty
    /// keys are equal to each other and to <c>default</c>.
    /// </remarks>
    [Serializable]
    public struct LocKey : IEquatable<LocKey>
    {
        // The serialized field name is part of the data format of every asset
        // that stores a LocKey: renaming it requires [FormerlySerializedAs].
        [SerializeField] private string Value;

        /// <summary>Creates a key. Null or empty <paramref name="value"/> gives an empty key.</summary>
        public LocKey(string value)
        {
            Value = string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Builds a key from a format string, e.g.
        /// <c>LocKey.Format("enemy_{0}_name", id)</c>.
        /// </summary>
        public static LocKey Format(string format, params object[] args)
            => new(string.Format(format, args));

        /// <summary>True when the key is null or empty.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public static implicit operator string(LocKey key) => key.Value;
        public static implicit operator LocKey(string value) => new(value);

        public override string ToString() => Value ?? string.Empty;

        // Unity serializes a null string as "", so a key loaded from an asset
        // and a key created in code must compare by normalized value.
        public bool Equals(LocKey other) => ToString() == other.ToString();
        public override bool Equals(object obj) => obj is LocKey other && Equals(other);
        public override int GetHashCode() => ToString().GetHashCode();

        public static bool operator ==(LocKey left, LocKey right) => left.Equals(right);
        public static bool operator !=(LocKey left, LocKey right) => !left.Equals(right);
    }
}
