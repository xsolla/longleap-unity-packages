using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Transport to the token endpoints. The UnityWebRequest implementation
    // lives in the XSOLLA_ID assembly; only the contract is here, so the whole
    // state machine can be tested without network.
    //
    // No method throws: failures are returned as a code in the result.
    public interface IXsollaIdTokenClient
    {
        // Exchange the one-time code for tokens (authorization_code + PKCE).
        Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri);

        // Refresh using the refresh token.
        Task<TokenResponse> RefreshAsync(string refreshToken);

        // Who this is. Needed only for the name in the UI — without the
        // profile/email scopes only sub comes back.
        Task<UserInfoResponse> GetUserInfoAsync(string accessToken);

        // Token revocation. Best-effort: this Xsolla ID endpoint has returned
        // 405 before, so signing out must not depend on it.
        Task RevokeAsync(string accessToken);
    }

    public readonly struct TokenResponse
    {
        public AccessToken Access { get; }
        public RefreshToken Refresh { get; }
        public long ExpiresAtUnixMs { get; }

        // Raw id_token, if the server sent one. Parsed elsewhere; kept as a
        // string here so the contract does not pull in a JWT parser.
        public string RawIdToken { get; }

        public XsollaIdErrorCode? Error { get; }
        public string Message { get; }

        public bool IsSuccess => Error == null;

        private TokenResponse(
            AccessToken access, RefreshToken refresh, long expiresAtUnixMs, string rawIdToken,
            XsollaIdErrorCode? error, string message)
        {
            Access = access;
            Refresh = refresh;
            ExpiresAtUnixMs = expiresAtUnixMs;
            RawIdToken = rawIdToken;
            Error = error;
            Message = message;
        }

        public static TokenResponse Ok(AccessToken access, RefreshToken refresh, long expiresAtUnixMs, string rawIdToken = null) =>
            new TokenResponse(access, refresh, expiresAtUnixMs, rawIdToken, null, null);

        public static TokenResponse Failed(XsollaIdErrorCode code, string message) =>
            new TokenResponse(AccessToken.None, RefreshToken.None, 0, null, code, message);
    }

    public readonly struct UserInfoResponse
    {
        public XsollaUserId Subject { get; }

        // username, preferred_username or email — whichever was found.
        public string DisplayName { get; }

        // The username and name claims as is, without picking.
        //
        // Not a duplicate of DisplayName: that one is picked from four claims
        // by priority (XsollaIdTokenClient.PickDisplayName) and suits places
        // with room for a single line. The expanded account card shows two
        // distinct values, and one cannot stand in for the other: for an
        // account without its own username the server issues a generated
        // id<digits>, which has nothing to do with the profile name.
        //
        // Both may be null: the claim may be absent from the response.
        public string UserName { get; }
        public string FullName { get; }

        // Profile picture url. None is normal: the picture claim is optional,
        // and whether a given Xsolla ID instance returns it is not known in
        // advance.
        public AvatarUrl Picture { get; }

        public XsollaIdErrorCode? Error { get; }
        public string Message { get; }

        public bool IsSuccess => Error == null;

        private UserInfoResponse(
            XsollaUserId subject, string displayName, AvatarUrl picture,
            string userName, string fullName,
            XsollaIdErrorCode? error, string message)
        {
            Subject = subject;
            DisplayName = displayName;
            Picture = picture;
            UserName = userName;
            FullName = fullName;
            Error = error;
            Message = message;
        }

        // picture is an optional parameter rather than an overload, just like
        // rawIdToken in TokenResponse.Ok above. It is `= default` rather than
        // `= AvatarUrl.None`: None is a property, and a default value must be a
        // compile-time constant.
        //
        // userName and fullName also go at the end with defaults: Ok has few
        // callers, but none of them should break because of two fields needed
        // only by the expanded card.
        public static UserInfoResponse Ok(
            XsollaUserId subject, string displayName, AvatarUrl picture = default,
            string userName = null, string fullName = null) =>
            new UserInfoResponse(subject, displayName, picture, userName, fullName, null, null);

        public static UserInfoResponse Failed(XsollaIdErrorCode code, string message) =>
            new UserInfoResponse(XsollaUserId.None, null, AvatarUrl.None, null, null, code, message);
    }
}
