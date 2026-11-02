using System;
using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Stub for when sign-in is not available: a portal build, the XSOLLA_ID
    // define turned off, or an unfilled config.
    //
    // Exists so game code has no #if: the UI checks IsAvailable and hides the
    // account block (hides, not disables — the same way other UI elements are
    // hidden when a feature does not exist on the current platform).
    public sealed class UnavailableXsollaIdAuth : IXsollaIdAuth
    {
        public bool IsAvailable => false;

        // Nothing to wait for: the answer is final from the first frame.
        public bool IsReady => true;

        public bool IsSignedIn => false;

        // There is never a sign-in here, so there can be no player gesture.
        public XsollaIdSignInOrigin LastSignInOrigin => XsollaIdSignInOrigin.Restored;

        public XsollaIdSession Current => null;

        // Never raised. Declared so UI subscriptions need no checks; add/remove
        // are empty, so nothing can leak.
        public event Action<XsollaIdSession> SignedIn { add { } remove { } }
        public event Action SignedOut { add { } remove { } }
        public event Action<XsollaIdSession> SessionUpdated { add { } remove { } }

        public Task<XsollaIdResult> SignInAsync() => Task.FromResult(XsollaIdResult.Unavailable());

        public Task<XsollaIdResult> RestoreAsync() => Task.FromResult(XsollaIdResult.Unavailable());

        public Task<XsollaIdResult> RefreshAsync() => Task.FromResult(XsollaIdResult.Unavailable());

        public Task SignOutAsync() => Task.CompletedTask;
    }
}
