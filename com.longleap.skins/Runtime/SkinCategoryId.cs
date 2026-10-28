using System;

namespace LongLeap.Skins
{
    /// <summary>
    /// Категория скинов: доска, голова, торс, ноги. Из выбранного в каждой
    /// категории и складывается внешний вид персонажа — по одному скину на
    /// категорию, как в <c>ISkinTarget.SetSkins</c>.
    ///
    /// Это НАШ идентификатор, а не спайновый (в спайне категорий нет, там
    /// плоский список скинов), поэтому регистр приводится к нижнему: «Board» и
    /// «board» из каталога и из кода — одна и та же вкладка.
    /// </summary>
    public readonly struct SkinCategoryId : IEquatable<SkinCategoryId>
    {
        private readonly string _value;

        /// <summary>Нормализованный id категории или пустая строка, если он не задан.</summary>
        public string Value => _value ?? "";

        /// <summary>Id не задан (default(SkinCategoryId)).</summary>
        public bool IsNone => _value == null;

        public SkinCategoryId(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));

            var trimmed = id.Trim().ToLowerInvariant();
            if (trimmed.Length == 0)
                throw new ArgumentException("Id категории не может быть пустым.", nameof(id));

            _value = trimmed;
        }

        public bool Equals(SkinCategoryId other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is SkinCategoryId other && Equals(other);

        public override int GetHashCode() => _value != null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

        public override string ToString() => _value ?? "";

        public static implicit operator SkinCategoryId(string id) => new SkinCategoryId(id);

        public static bool operator ==(SkinCategoryId left, SkinCategoryId right) => left.Equals(right);

        public static bool operator !=(SkinCategoryId left, SkinCategoryId right) => !left.Equals(right);
    }
}
