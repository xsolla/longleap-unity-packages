namespace LongLeap.XsollaId
{
    // Reason a sign-in failed. The code is separate from the text because the
    // UI reacts to different outcomes in opposite ways: a window closed by the
    // player is not an error and shows nothing, while a network failure needs
    // a "retry" button.
    //
    // This is where the contract differs from LongLeap.Core.Auth.AuthResult.Failure,
    // which has only a string: that result is never shown to the player.
    public enum XsollaIdErrorCode
    {
        // The player closed the sign-in window or pressed "cancel". Stay silent.
        Cancelled,

        // The browser blocked the popup window. The player needs a hint.
        PopupBlocked,

        // Sign-in is not available in this build (portal) or is disabled by the
        // define.
        Unavailable,

        // No config: clientId, issuer or redirect uri is not set.
        NotConfigured,

        // Network unavailable, DNS, connection dropped.
        Network,

        // No response within the allotted time.
        Timeout,

        // The state parameter did not match the expected one — a possible CSRF
        // attack or a stale request. The code is NOT exchanged for a token in
        // this case.
        InvalidState,

        // The server rejected the code exchange or returned garbage.
        TokenExchangeFailed,

        // There is no session, or the refresh token is no longer accepted
        // (expired, revoked). A normal outcome after 30 days, not a failure:
        // quietly fall back to guest.
        SessionExpired,

        // The profile picture could not be obtained: no url, the server returned
        // non-2xx, or the response was not an image. Sign-in is NOT affected —
        // the UI just shows a placeholder.
        PictureUnavailable
    }
}
