namespace LongLeap.XsollaId.UI
{
    // What the account widget is showing right now.
    //
    // A dedicated state rather than ad-hoc checks: IXsollaIdAuth has three
    // independent flags (IsAvailable/IsReady/IsSignedIn) plus two tasks in
    // flight, and reducing them to a picture in two places is a guaranteed
    // desync.
    public enum AccountWidgetState
    {
        // No sign-in at all: the block is hidden entirely, id.xsolla.com is not touched.
        Hidden,

        // The answer is not final yet — too early to show a guest.
        Checking,

        // Sign-in button.
        SignedOut,

        // The sign-in window is open.
        SigningIn,

        // Sign-in button plus a line with the reason the last attempt failed.
        SignedOutWithError,

        // Avatar, name and "sign out".
        SignedIn,

        // Sign-out in flight: "sign out" is not clickable.
        SigningOut
    }

    // Pure decisions of the account widget.
    //
    // No UnityEngine calls, so it is covered by EditMode tests without a scene,
    // a prefab or a WebGL build. This is the one part of the widget where a bug
    // is invisible: SetActive on the wrong object shows immediately, a wrong
    // state priority does not.
    public static class XsollaIdAccountWidgetLogic
    {
        // Localization keys for failure reasons. Kept here rather than in the
        // widget: mapping code to key is part of the decision, and a test checks
        // that the enum is covered completely.
        public const string ErrorKeyPopupBlocked = "xsolla_err_popup_blocked";
        public const string ErrorKeyNetwork = "xsolla_err_network";
        public const string ErrorKeyTimeout = "xsolla_err_timeout";
        public const string ErrorKeyFailed = "xsolla_err_failed";
        public const string ErrorKeyUnavailable = "xsolla_err_unavailable";

        // The order of checks matters — see the comments along the way.
        public static AccountWidgetState Resolve(
            bool serviceExists,
            bool isAvailable,
            bool isReady,
            bool readyTimedOut,
            bool isSignedIn,
            bool signInPending,
            bool signOutPending,
            bool hasError)
        {
            // Unavailability is a final answer and overrides everything else,
            // so the block is hidden, not merely disabled.
            if (serviceExists && !isAvailable)
                return AccountWidgetState.Hidden;

            // No service yet: the game may create it AFTER the widget's
            // OnEnable. Showing a guest would be wrong — we simply don't know.
            if (!serviceExists)
                return AccountWidgetState.Checking;

            if (isSignedIn && signOutPending)
                return AccountWidgetState.SigningOut;

            // ABOVE signInPending on purpose: SignedIn fires from Adopt before
            // the SignInAsync task completes. The other way round, the line
            // would flash back to "signing in…" after a successful sign-in.
            if (isSignedIn)
                return AccountWidgetState.SignedIn;

            // ABOVE hasError: a new attempt overrides the previous error,
            // otherwise the player presses the button and sees the old complaint.
            if (signInPending)
                return AccountWidgetState.SigningIn;

            if (!isReady && !readyTimedOut)
                return AccountWidgetState.Checking;

            if (hasError)
                return AccountWidgetState.SignedOutWithError;

            return AccountWidgetState.SignedOut;
        }

        // Whether the expanded account card is visible.
        //
        // Expansion is NOT another enum state but a separate flag: it is
        // orthogonal to the rest (the card can be open in SigningOut while
        // "sign out" is not clickable), and adding it to Resolve would double
        // the states. But the condition must not live inline in the widget
        // either: forgetting to collapse the card on sign-out is exactly the
        // kind of bug nobody sees, because it only shows up on the next sign-in.
        public static bool ShowsExpandedPanel(AccountWidgetState state, bool expanded) =>
            expanded && (state == AccountWidgetState.SignedIn || state == AccountWidgetState.SigningOut);

        // Length truncation and nothing else.
        //
        // Separate from ShortName: a username is not an e-mail address and has
        // no '@' to cut at — but its length still needs a limit, otherwise a
        // generated id<digits> stretches the card.
        public static string Shorten(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return Truncate(value.Trim(), maxChars);
        }

        // Name for the widget line: local part of the e-mail plus length truncation.
        //
        // Returns null when there is nothing to show — the widget substitutes a
        // localized fallback. An empty string is also turned into null: for the
        // UI they are the same, and the caller needs only one check.
        public static string ShortName(string displayName, int maxChars)
        {
            string local = DisplayNameFormat.LocalPart(displayName);

            if (string.IsNullOrWhiteSpace(local))
                return null;

            return Truncate(local, maxChars);
        }

        // Localization key to show the player, or null if there is nothing to say.
        public static string ErrorKey(XsollaIdErrorCode code)
        {
            switch (code)
            {
                // The player closed the window — not an error, and there is no
                // point complaining about the player's own choice.
                case XsollaIdErrorCode.Cancelled:

                // Expected outcome after 30 days: silently become a guest.
                case XsollaIdErrorCode.SessionExpired:
                    return null;

                case XsollaIdErrorCode.PopupBlocked:
                    return ErrorKeyPopupBlocked;

                case XsollaIdErrorCode.Network:
                    return ErrorKeyNetwork;

                case XsollaIdErrorCode.Timeout:
                    return ErrorKeyTimeout;

                // Indistinguishable for the player: sign-in did not happen, try
                // again. Failure.Message in the console tells the difference.
                case XsollaIdErrorCode.Unavailable:
                case XsollaIdErrorCode.NotConfigured:
                    return ErrorKeyUnavailable;

                // InvalidState, TokenExchangeFailed, PictureUnavailable and
                // anything added to the enum later. A generic message beats
                // silence: the player reads silence as "the button is broken".
                default:
                    return ErrorKeyFailed;
            }
        }

        private static string Truncate(string value, int maxChars)
        {
            // Truncation disabled.
            if (maxChars <= 0 || value.Length <= maxChars)
                return value;

            int cut = maxChars - 1;

            // Do not split a surrogate pair: half an emoji renders as garbage.
            // A pair takes two chars, so step back by one.
            if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
                cut--;

            return value.Substring(0, cut) + "…";
        }
    }
}
