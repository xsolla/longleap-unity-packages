using System;
using LongLeap.XsollaId;

namespace LongLeap.Accounts
{
    // Why the screen is busy — the game picks the text.
    public enum AccountScreenBusy
    {
        // The Xsolla ID sign-in window is open.
        SigningIn,

        // Signed in; reading the account save from the server.
        CheckingSave
    }

    // The startup screen: "Sign in with Xsolla ID" or "Play as guest".
    //
    // Drawn by the game (its background, its buttons, its language); the flow
    // behind it lives in AccountGate. The screen makes no decisions: it shows
    // the state it is told to show and reports clicks through the handlers.
    //
    // WARNING: invoke SignInClicked SYNCHRONOUSLY from the button's onClick,
    // without coroutines or awaits in between. AccountGate calls SignInAsync
    // right inside the handler, and a popup opened outside a user gesture is
    // blocked by the browser.
    public interface IAccountScreen
    {
        // Set by AccountGate before every Show*; null means "no such button now".
        Action SignInClicked { get; set; }
        Action GuestClicked { get; set; }
        Action RetryClicked { get; set; }

        // The choice: sign in or play as guest. lastError — why the previous
        // sign-in attempt failed (popup blocked, network); null shows nothing.
        // Cancelled and SessionExpired never come here: they are not errors.
        void ShowChoice(XsollaIdErrorCode? lastError);

        // Waiting: the buttons are disabled.
        void ShowBusy(AccountScreenBusy reason);

        // Signed in, but the account save could not be read, so guest progress
        // can't be merged safely. Offer "Try again" (RetryClicked) or "Play as
        // guest" (GuestClicked).
        void ShowServerError();

        // The choice is made — remove the screen.
        void Close();
    }
}
