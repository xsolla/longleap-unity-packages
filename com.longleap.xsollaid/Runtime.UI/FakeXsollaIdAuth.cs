#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace LongLeap.XsollaId.UI
{
    // Fake sign-in service — EDITOR ONLY.
    //
    // Real sign-in is never available in the editor: XsollaIdWebBrowser.IsSupported
    // lives under #if UNITY_WEBGL && !UNITY_EDITOR, so the factory returns
    // UnavailableXsollaIdAuth and the widget hides itself. Without a fake the
    // "signed in" layout cannot be seen without building WebGL and running the
    // local dev server.
    //
    // The whole file is under #if UNITY_EDITOR and is not referenced by any
    // serialized field, so not a byte of it reaches a build. There are
    // deliberately no serialized references to it: a build would get a
    // Missing Script otherwise.
    public sealed class FakeXsollaIdAuth : IXsollaIdAuth
    {
        private FakeXsollaIdAuth(bool available, bool ready, XsollaIdSession session)
        {
            IsAvailable = available;
            IsReady = ready;
            Current = session;
        }

        public bool IsAvailable { get; }
        public bool IsReady { get; }
        public bool IsSignedIn => Current != null;

        // The fake serves previews and layout tests, not save switching, so the
        // origin is fixed and never interactive.
        public XsollaIdSignInOrigin LastSignInOrigin => XsollaIdSignInOrigin.Restored;
        public XsollaIdSession Current { get; private set; }

        // Never raised: the state is set at construction.
        public event Action<XsollaIdSession> SignedIn { add { } remove { } }
        public event Action SignedOut { add { } remove { } }
        public event Action<XsollaIdSession> SessionUpdated { add { } remove { } }

        // Factory names do NOT repeat the event names: SignedIn and SignedOut
        // are already taken by interface members.
        public static IXsollaIdAuth Guest() => new FakeXsollaIdAuth(true, true, null);

        public static IXsollaIdAuth Restoring() => new FakeXsollaIdAuth(true, false, null);

        public static IXsollaIdAuth Unavailable() => new FakeXsollaIdAuth(false, true, null);

        // userName and fullName are optional: the expanded card shows them
        // separately, and a preview without them would only exercise the
        // fallback path (no claims), not the normal one.
        public static IXsollaIdAuth SignedInAs(
            string displayName, string userName = null, string fullName = null) => new FakeXsollaIdAuth(
            true, true,
            new XsollaIdSession(
                new XsollaUserId("preview-sub"), displayName,
                new AccessToken("preview-access"), new RefreshToken("preview-refresh"),
                expiresAtUnixMs: long.MaxValue,
                picture: AvatarUrl.None,
                profileFetched: true,
                userName: userName,
                fullName: fullName));

        // Opens no windows and makes no network calls.
        public Task<XsollaIdResult> SignInAsync() =>
            Task.FromResult(IsSignedIn
                ? new XsollaIdResult.Success(Current)
                : XsollaIdResult.Cancelled());

        public Task<XsollaIdResult> RestoreAsync() =>
            Task.FromResult(IsSignedIn
                ? new XsollaIdResult.Success(Current)
                : XsollaIdResult.Expired());

        public Task<XsollaIdResult> RefreshAsync() => RestoreAsync();

        public Task SignOutAsync()
        {
            Current = null;
            return Task.CompletedTask;
        }
    }
}
#endif
