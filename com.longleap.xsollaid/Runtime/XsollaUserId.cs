using System;

namespace LongLeap.XsollaId
{
    // Идентификатор пользователя Xsolla ID — клейм `sub` из OIDC-токена.
    //
    // ЭТО НЕ Firebase uid. Под ним нельзя читать/писать cloud_saves и нельзя
    // подписывать запросы к RTDB: там принимаются только токены, выпущенные
    // Firebase. Отдельный тип нужен именно для того, чтобы эти два
    // идентификатора нельзя было перепутать в коде.
    //
    // В отличие от LongLeap.Core.Auth.UserId конструктор запрещает и null, и
    // пустую строку: у UserId `new UserId(null)` молча даёт None, и на этом
    // легко не заметить, что личности на самом деле нет. Пустое значение
    // получают только через None.
    public readonly struct XsollaUserId : IEquatable<XsollaUserId>
    {
        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public XsollaUserId(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException(
                    "XsollaUserId: value cannot be empty — use None.", nameof(value));

            Value = value;
        }

        public static XsollaUserId None => default;

        public bool Equals(XsollaUserId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is XsollaUserId other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;

        // sub не секрет, но и в логи его лить незачем — он связывает записи
        // между сервисами. Печатаем только факт наличия.
        public override string ToString() => IsValid ? "XsollaUserId(set)" : "XsollaUserId(none)";

        public static bool operator ==(XsollaUserId left, XsollaUserId right) => left.Equals(right);
        public static bool operator !=(XsollaUserId left, XsollaUserId right) => !left.Equals(right);
    }
}
