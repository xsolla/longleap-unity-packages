namespace LongLeap.XsollaId
{
    // Snapshot of an Xsolla ID session: who is signed in and what proves it.
    //
    // Immutable. A token refresh does not mutate the object but creates a new
    // one — so SignedIn subscribers can never see a half-updated state, and
    // "is it the same session?" reduces to a reference comparison.
    public sealed class XsollaIdSession
    {
        // sub from the token. The only thing that can be treated as a stable
        // user key — but NOT a Firebase uid, see XsollaUserId.
        public XsollaUserId Subject { get; }

        // Name for the UI: username or email from userinfo. May be null — with
        // only the openid scope requested there is nothing to show.
        public string DisplayName { get; }

        // Profile picture url. None is normal, see UserInfoResponse.Picture.
        public AvatarUrl Picture { get; }

        // Raw username and name claims — shown by the expanded account card,
        // which has a separate line for each. Why DisplayName alone is not
        // enough — see UserInfoResponse.UserName.
        //
        // Both may be null, and that is normal: an account without a name in
        // its profile has no name claim at all.
        public string UserName { get; }
        public string FullName { get; }

        // Whether userinfo has already been requested for THIS identity.
        //
        // Needs its own flag rather than "name is non-empty": the picture claim
        // may be missing entirely, and "no picture → fetch again" would hit
        // userinfo on every token refresh, forever. The flag is persisted with
        // the session, so the catch-up request happens exactly once per player.
        public bool ProfileFetched { get; }

        public AccessToken Access { get; }

        public RefreshToken Refresh { get; }

        // Access token expiry, unix time in milliseconds.
        public long ExpiresAtUnixMs { get; }

        // picture, profileFetched, userName and fullName go ONLY at the end and
        // only with default values. Moving picture next to displayName, where
        // it belongs by meaning, is not possible: several call sites pass
        // arguments positionally, and reordering would silently bind them to
        // the wrong parameters.
        //
        // profileFetched defaults to false because the safe answer to "was it
        // requested?" is "no": a code path that forgets to set it costs one
        // extra request, not a permanently lost picture.
        public XsollaIdSession(
            XsollaUserId subject,
            string displayName,
            AccessToken access,
            RefreshToken refresh,
            long expiresAtUnixMs,
            AvatarUrl picture = default,
            bool profileFetched = false,
            string userName = null,
            string fullName = null)
        {
            Subject = subject;
            DisplayName = displayName;
            Access = access;
            Refresh = refresh;
            ExpiresAtUnixMs = expiresAtUnixMs;
            Picture = picture;
            ProfileFetched = profileFetched;
            UserName = userName;
            FullName = fullName;
        }

        // Name to show the player: if DisplayName is an email address, the
        // domain is cut off. The raw claim stays in DisplayName — it is needed
        // in logs and the debug panel. See DisplayNameFormat.
        public string ShortDisplayName => DisplayNameFormat.LocalPart(DisplayName);

        // There is something to refresh with, without the player's involvement.
        public bool CanRefresh => Refresh.IsValid;

        // Whether the access token has expired, with a margin of marginMs.
        //
        // The margin prevents sending a request with a token that expires in
        // flight. The caller supplies it — there is deliberately no constant
        // here: the refresh policy belongs to XsollaIdAuth, not to the snapshot.
        public bool IsExpired(long nowUnixMs, long marginMs) =>
            !Access.IsValid || nowUnixMs >= ExpiresAtUnixMs - marginMs;

        // Tokens are never printed: the token types hide them already, but this
        // is a reminder.
        public override string ToString() =>
            $"XsollaIdSession({Subject}, displayName={(string.IsNullOrEmpty(DisplayName) ? "none" : "set")}, " +
            $"userName={(string.IsNullOrEmpty(UserName) ? "none" : "set")}, " +
            $"fullName={(string.IsNullOrEmpty(FullName) ? "none" : "set")}, " +
            $"picture={(Picture.IsValid ? "set" : "none")}, profileFetched={ProfileFetched}, " +
            $"expiresAt={ExpiresAtUnixMs}, canRefresh={CanRefresh})";
    }
}
