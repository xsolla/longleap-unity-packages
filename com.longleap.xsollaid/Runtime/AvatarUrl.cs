using System;

namespace LongLeap.XsollaId
{
    // Validated profile picture url (the picture claim from userinfo).
    //
    // A separate type rather than a string, for the same reason as
    // XsollaUserId: the url comes from the server, and "validated" is a
    // property of the type, not a promise from the caller. Raw text cannot be
    // passed to the avatar loader.
    //
    // Unlike XsollaUserId, the constructor does NOT throw. sub is required and
    // must be rejected when missing; picture is an optional claim, and garbage
    // in it must not break userinfo parsing or sign-in. An unusable value
    // simply becomes None, and the UI shows a placeholder.
    public readonly struct AvatarUrl : IEquatable<AvatarUrl>
    {
        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        private AvatarUrl(string value)
        {
            Value = value;
        }

        public static AvatarUrl None => default;

        public static AvatarUrl TryParse(string raw) =>
            IsAcceptable(raw) ? new AvatarUrl(raw.Trim()) : None;

        // Only absolute https is accepted. In order of severity:
        //
        //   http on an https page is blocked by the browser as mixed content,
        //     without a clear message — rejecting it upfront is more honest;
        //   data: and javascript: would not be opened by UnityWebRequest in
        //     WebGL anyway, and passing an arbitrary scheme from a server
        //     response to the loader is a bad idea security-wise;
        //   a relative url has nothing to resolve against: there is no base.
        public static bool IsAcceptable(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            return Uri.TryCreate(raw.Trim(), UriKind.Absolute, out Uri uri) &&
                   uri.Scheme == Uri.UriSchemeHttps;
        }

        public bool Equals(AvatarUrl other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AvatarUrl other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;

        // The url may contain a signed CDN token, so it is never logged — only
        // its presence is printed. Same as AccessToken and XsollaUserId.
        public override string ToString() => IsValid ? "AvatarUrl(set)" : "AvatarUrl(none)";

        public static bool operator ==(AvatarUrl left, AvatarUrl right) => left.Equals(right);
        public static bool operator !=(AvatarUrl left, AvatarUrl right) => !left.Equals(right);
    }
}
