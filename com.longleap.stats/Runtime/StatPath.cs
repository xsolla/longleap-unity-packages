using System;

namespace LongLeap.Stats
{
    public readonly struct StatPath : IEquatable<StatPath>
    {
        private readonly string _value;
        private readonly int _depth;
        private readonly string _lastSegment;
        private readonly bool _isPattern;

        public string Value => _value ?? "";
        public int Depth => _depth;
        public string LastSegment => _lastSegment ?? "";
        public bool IsPattern => _isPattern;
        public bool IsNone => _value == null;

        public StatPath Parent
        {
            get
            {
                if (_value == null) return default;
                int colonIdx = _value.LastIndexOf(':');
                if (colonIdx < 0) return default;
                return new StatPath(_value.Substring(0, colonIdx));
            }
        }

        public StatPath(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var trimmed = path.Trim().ToLowerInvariant();
            if (trimmed.Length == 0) throw new ArgumentException("Path cannot be empty", nameof(path));
            if (trimmed.Contains("::")) throw new ArgumentException("Path cannot contain '::'", nameof(path));
            if (trimmed[0] == ':' || trimmed[trimmed.Length - 1] == ':')
                throw new ArgumentException("Path cannot start or end with ':'", nameof(path));

            _value = trimmed;
            int colonIdx = _value.LastIndexOf(':');
            _lastSegment = colonIdx >= 0 ? _value.Substring(colonIdx + 1) : _value;
            _depth = _value.Split(':').Length;
            _isPattern = _value.Contains('*');
        }

        public StatPath Append(string segment)
        {
            if (segment == null) throw new ArgumentNullException(nameof(segment));
            var trimmed = segment.Trim().ToLowerInvariant();
            if (trimmed.Length == 0) throw new ArgumentException("Segment cannot be empty", nameof(segment));
            if (trimmed.Contains(':')) throw new ArgumentException("Segment cannot contain ':'", nameof(segment));
            return new StatPath(_value + ":" + trimmed);
        }

        public bool Matches(StatPath concrete)
        {
            if (concrete.IsNone) return false;
            if (!_isPattern) return Equals(concrete);

            var patternSegments = _value.Split(':');
            var concreteSegments = concrete._value.Split(':');

            if (patternSegments.Length != concreteSegments.Length) return false;

            for (int i = 0; i < patternSegments.Length; i++)
            {
                if (patternSegments[i] != "*" &&
                    !string.Equals(patternSegments[i], concreteSegments[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        public bool Equals(StatPath other) =>
            string.Equals(_value, other._value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is StatPath other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static implicit operator StatPath(string path) => new StatPath(path);

        public static bool operator ==(StatPath left, StatPath right) => left.Equals(right);
        public static bool operator !=(StatPath left, StatPath right) => !left.Equals(right);
    }
}
