using System;

namespace LongLeap.XsollaId.Accounts
{
    /// <summary>
    /// Why the screen is busy — the game picks the text.
    /// </summary>
    public enum AccountScreenBusy
    {
        /// <summary>The Xsolla ID sign-in window is open.</summary>
        SigningIn,

        /// <summary>Signed in; reading the account save from the server.</summary>
        CheckingSave
    }

    /// <summary>
    /// The startup screen: "Sign in with Xsolla ID" or "Play as guest".
    /// </summary>
    /// <remarks>
    /// Drawn by the game (its background, its buttons, its language); the flow
    /// behind it lives in <see cref="AccountGate"/>. The screen makes no
    /// decisions: it shows the state it is told to show and reports clicks
    /// through the handlers.
    ///
    /// WARNING: invoke <see cref="SignInClicked"/> SYNCHRONOUSLY from the
    /// button's <c>onClick</c>, without coroutines or awaits in between.
    /// <see cref="AccountGate"/> calls <c>SignInAsync</c> right inside the
    /// handler, and a popup opened outside a user gesture is blocked by the
    /// browser.
    /// </remarks>
    public interface IAccountScreen
    {
        /// <summary>
        /// "Sign in" was clicked. Set by <see cref="AccountGate"/> before every
        /// <c>Show*</c> call; <c>null</c> means there is no such button now.
        /// </summary>
        Action SignInClicked { get; set; }

        /// <summary>
        /// "Play as guest" was clicked. Set before every <c>Show*</c> call;
        /// <c>null</c> means there is no such button now.
        /// </summary>
        Action GuestClicked { get; set; }

        /// <summary>
        /// "Try again" was clicked. Set before every <c>Show*</c> call;
        /// <c>null</c> means there is no such button now.
        /// </summary>
        Action RetryClicked { get; set; }

        /// <summary>
        /// The choice: sign in or play as guest.
        /// </summary>
        /// <param name="lastError">Why the previous sign-in attempt failed (popup
        /// blocked, network); <c>null</c> shows nothing. <c>Cancelled</c> and
        /// <c>SessionExpired</c> never come here: they are not errors.</param>
        void ShowChoice(XsollaIdErrorCode? lastError);

        /// <summary>
        /// Waiting: the buttons must be disabled. The handlers stay set, so the
        /// screen disables them itself.
        /// </summary>
        void ShowBusy(AccountScreenBusy reason);

        /// <summary>
        /// Signed in, but the account save could not be read, so guest progress
        /// can't be merged safely. Offer "Try again" (<see cref="RetryClicked"/>)
        /// or "Play as guest" (<see cref="GuestClicked"/>).
        /// </summary>
        void ShowServerError();

        /// <summary>
        /// The choice is made — remove the screen. Called once.
        /// </summary>
        void Close();
    }
}
