using System;
using System.Text.RegularExpressions;

namespace LongLeap.Core.CloudSaves
{
    [Serializable]
    public readonly struct CloudSaveKey : IEquatable<CloudSaveKey>
    {
        private static readonly Regex ValidPattern = new(@"^[^.$#\[\]/]+$", RegexOptions.Compiled);
        private static readonly char[] ForbiddenChars = { '.', '$', '#', '[', ']', '/' };

        public string Value { get; }

        public CloudSaveKey(string key)
        {
            if (!string.IsNullOrEmpty(key) && !ValidPattern.IsMatch(key))
                throw new ArgumentException($"CloudSaveKey: key '{key}' contains forbidden characters (. $ # [ ] /).");
            Value = key ?? "";
        }

        public bool Equals(CloudSaveKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CloudSaveKey other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value;
        public static bool operator ==(CloudSaveKey left, CloudSaveKey right) => left.Equals(right);
        public static bool operator !=(CloudSaveKey left, CloudSaveKey right) => !left.Equals(right);
    }
}
