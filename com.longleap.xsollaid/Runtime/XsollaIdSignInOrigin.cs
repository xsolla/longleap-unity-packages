namespace LongLeap.XsollaId
{
    // Where the current session came from.
    //
    // Typically only the game's save switcher needs to tell these apart.
    // SignedIn fires both on a button press and on the silent session restore
    // at startup, but asking the player "which save to keep?" is only
    // acceptable in the first case: in the second they pressed nothing, and a
    // dialog in the middle of loading would look like a bug.
    public enum XsollaIdSignInOrigin
    {
        // The player pressed the sign-in button and the Xsolla ID window opened.
        Interactive,

        // The session was restored from the saved refresh token at startup. No
        // player gesture was involved.
        Restored,

        // The token of an already signed-in player was refreshed. The identity
        // did not change.
        Refreshed
    }
}
