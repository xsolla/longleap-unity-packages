namespace LongLeap.XsollaId
{
    // How DisplayName is shown to the player.
    //
    // A separate pure method rather than logic in the widget: userinfo provides
    // the name through four different claims (XsollaIdTokenClient.PickDisplayName),
    // the last of which is email. So for some players the "name" is an email
    // address, and it must not be shown in full: the main menu ends up in
    // screenshots and streams.
    public static class DisplayNameFormat
    {
        // user@mail.com → user. A name without '@' is returned as is.
        //
        // Splits at the FIRST '@', although RFC 5321 says to use the last one
        // when parsing an address. The goal here is not to parse the address but
        // to guarantee the domain never reaches the screen — splitting at the
        // first '@' always achieves that.
        //
        // An empty local part (@mail.com, or just @) is garbage, not an address:
        // it is returned as is, because an empty spot on screen is worse than an
        // odd-looking name.
        public static string LocalPart(string displayName)
        {
            // null stays null rather than becoming an empty string: callers
            // already handle it (`?? "-"` in XsollaIdDebugPanel), and replacing
            // it with "" would silently bypass that check.
            if (displayName == null)
                return null;

            string trimmed = displayName.Trim();

            if (trimmed.Length == 0)
                return string.Empty;

            int at = trimmed.IndexOf('@');

            // -1 means there is no '@', 0 means an empty local part. Either way
            // there is nothing to cut.
            if (at <= 0)
                return trimmed;

            return trimmed.Substring(0, at);
        }
    }
}
