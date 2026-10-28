using System;

namespace LongLeap.Skins
{
    /// <summary>
    /// Имя скина — ровно как в Spine Editor.
    ///
    /// Регистр НЕ приводится, в отличие от <see cref="CurrencyId"/> и
    /// <see cref="SkinCategoryId"/>: это не наш id, а ключ, по которому
    /// спайновый <c>FindSkin</c> ищет скин в скелете, и там регистр значим.
    /// Привели бы к нижнему — «Punk» перестал бы находиться, причём молча:
    /// FindSkin просто вернул бы null, и персонаж остался бы в базовом виде.
    ///
    /// Обрезаются только пробелы по краям (имена приезжают копированием из
    /// Spine) и запрещён перевод строки: сейв склеивает список скинов через
    /// него, и скин с переводом строки внутри развалил бы весь список.
    /// </summary>
    public readonly struct SkinId : IEquatable<SkinId>
    {
        private readonly string _value;

        /// <summary>Имя скина или пустая строка, если id не задан.</summary>
        public string Value => _value ?? "";

        /// <summary>Id не задан (default(SkinId)).</summary>
        public bool IsNone => _value == null;

        public SkinId(string skin)
        {
            if (skin == null) throw new ArgumentNullException(nameof(skin));

            var trimmed = skin.Trim();
            if (trimmed.Length == 0)
                throw new ArgumentException("Имя скина не может быть пустым.", nameof(skin));

            if (trimmed.IndexOf('\n') >= 0 || trimmed.IndexOf('\r') >= 0)
                throw new ArgumentException(
                    $"Имя скина не может содержать перевод строки: '{trimmed}'.", nameof(skin));

            _value = trimmed;
        }

        public bool Equals(SkinId other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is SkinId other && Equals(other);

        public override int GetHashCode() => _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static implicit operator SkinId(string skin) => new SkinId(skin);

        public static bool operator ==(SkinId left, SkinId right) => left.Equals(right);

        public static bool operator !=(SkinId left, SkinId right) => !left.Equals(right);
    }
}
