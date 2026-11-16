using System;

namespace LongLeap.XsollaId
{
    // Xsolla ID user identifier — the `sub` claim of the OIDC token.
    //
    // This is NOT a Firebase uid. It cannot be used to read/write cloud_saves or
    // to sign RTDB requests: those accept only Firebase-issued tokens. The
    // separate type exists so the two identifiers cannot be mixed up in code.
    //
    // Unlike LongLeap.Core.Auth.UserId, the constructor rejects both null and
    // the empty string: `new UserId(null)` silently yields None, which makes it
    // easy to miss that there is no identity at all. The empty value is
    // available only through None.
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

        // sub is not a secret, but there is no reason to put it in logs either —
        // it links records across services. Only its presence is printed.
        public override string ToString() => IsValid ? "XsollaUserId(set)" : "XsollaUserId(none)";

        public static bool operator ==(XsollaUserId left, XsollaUserId right) => left.Equals(right);
        public static bool operator !=(XsollaUserId left, XsollaUserId right) => !left.Equals(right);
    }
}
