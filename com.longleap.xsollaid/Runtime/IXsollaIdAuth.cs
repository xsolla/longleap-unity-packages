using System;
using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Interactive player sign-in through Xsolla ID.
    //
    // This is NOT LongLeap.Core.Auth.IAuthProvider and not a replacement for
    // it. They differ in frequency and interactivity:
    //
    //   IAuthProvider.LoginAsync() — "give me a valid Firebase token", called
    //     on EVERY RTDB request (FirebaseHttpClient calls it for all four
    //     verbs), so it must be silent and cheap.
    //   IXsollaIdAuth.SignInAsync() — "let the person sign in", opens a popup
    //     window and happens once, on a button press.
    //
    // An IAuthProvider able to open a browser window would stall loading on the
    // very first cloud save access. Hence two separate interfaces, and both
    // stay in the final architecture.
    //
    // Signing in CHANGES the save owner: before sign-in the save belongs to the
    // anonymous Firebase uid (guest branch), after it to the account id. This
    // interface does not do the switch and knows nothing about saves — see
    // IAccountCloudSaveScope and the game's save switcher listening to SignedIn
    // and SignedOut.
    public interface IXsollaIdAuth
    {
        // Whether sign-in is possible at all: platform, define and config all
        // line up. The single check the UI uses to hide the button, so screens
        // contain no #if.
        bool IsAvailable { get; }

        // The session restore attempt has finished (successfully or not). Until
        // then the state is "unknown yet", and the UI must not present the
        // guest state as the final answer.
        bool IsReady { get; }

        bool IsSignedIn { get; }

        // Where the current sign-in came from. Only meaningful when read from a
        // SignedIn handler — there the value refers to the event being raised.
        //
        // A property rather than a field in the SignedIn payload: changing the
        // event signature would affect every subscriber, while only one of them
        // (the save switcher) needs to know how the player signed in. It must
        // not ask the player which save to keep when the player pressed
        // nothing.
        XsollaIdSignInOrigin LastSignInOrigin { get; }

        // The current session, or null if the player is not signed in.
        XsollaIdSession Current { get; }

        // Whether the player can sign out from within the game. false where the
        // platform owns sign-in (GamePix SDK): it has no sign-out, and the very
        // next request would sign the player back in. The UI uses this to hide
        // the sign-out button.
        //
        // Default implementation so that other implementations and test fakes
        // do not have to change for this one exception.
        bool CanSignOut => true;

        // Subscribers must always unsubscribe in pairs (OnEnable/OnDisable).
        event Action<XsollaIdSession> SignedIn;
        event Action SignedOut;

        // The session was replaced but the player did not change: the token was
        // refreshed or the profile finished loading.
        //
        // Deliberately separate from SignedIn, which means the "signed out →
        // signed in" transition, and subscribers rely on exactly that. Without
        // this event Current would be swapped for a new object silently, and a
        // UI that cached the name or picture would quietly show stale data.
        event Action<XsollaIdSession> SessionUpdated;

        // Interactive sign-in: opens the Xsolla ID window.
        // Calling again while the previous call is still running returns THE
        // SAME task and does not open a second window.
        Task<XsollaIdResult> SignInAsync();

        // Silent restore from the saved refresh token. Called at startup; never
        // opens a window.
        Task<XsollaIdResult> RestoreAsync();

        // Force an access token refresh.
        Task<XsollaIdResult> RefreshAsync();

        // Revoke the token on the server and forget the session locally. The
        // local part always happens, even if the revoke request fails.
        Task SignOutAsync();
    }
}
