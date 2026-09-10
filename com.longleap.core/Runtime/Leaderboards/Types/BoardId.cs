using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LongLeap.Core.Leaderboards
{
    public readonly struct BoardId : IEquatable<BoardId>
    {
        private static readonly Regex ValidPattern = new Regex(
            @"^[a-zA-Z][a-zA-Z0-9_-]{0,39}$", RegexOptions.Compiled);

        private readonly string _value;

        public string Value => _value;

        public BoardId(string id)
        {
            if (string.IsNullOrEmpty(id) || !ValidPattern.IsMatch(id))
            {
                Debug.LogError($"BoardId: invalid identifier '{id}'. Using 'invalid_board_id'.");
                _value = "invalid_board_id";
                return;
            }
            _value = id;
        }

        public static BoardId None => default;

        public bool IsNone => _value == null;

        public bool IsValid => _value != null && _value != "invalid_board_id";

        public bool Equals(BoardId other) =>
            StringComparer.Ordinal.Equals(_value, other._value);

        public override bool Equals(object obj) =>
            obj is BoardId other && Equals(other);

        public override int GetHashCode() =>
            _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static bool operator ==(BoardId left, BoardId right) => left.Equals(right);

        public static bool operator !=(BoardId left, BoardId right) => !left.Equals(right);
    }
}
