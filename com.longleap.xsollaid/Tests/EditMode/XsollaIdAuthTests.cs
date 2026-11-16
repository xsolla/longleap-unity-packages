using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class XsollaIdAuthTests
    {
        private const string PictureUrl = "https://cdn.xsolla.com/a.png";

        private FakePlayerPrefs _prefs;
        private FakeDiscovery _discovery;
        private FakeBrowser _browser;
        private FakeTokenClient _tokens;
        private PlayerPrefsXsollaIdSessionStore _store;

        // Controllable clock: token-expiry tests must not depend on real time.
        private long _now;

        [SetUp]
        public void SetUp()
        {
            _prefs = new FakePlayerPrefs();
            _discovery = new FakeDiscovery();
            _browser = new FakeBrowser();
            _tokens = new FakeTokenClient();
            _store = new PlayerPrefsXsollaIdSessionStore(_prefs);
            _now = 1_000_000_000_000;
        }

        // Empty pageUrl, as in the editor: it passes the origin check, so the
        // tests do not depend on where the game is "opened". That behavior is
        // covered separately by OriginGuardTests.
        private XsollaIdAuth Create(
            XsollaIdConfig config = null, string pageUrl = "", IXsollaIdSessionStore store = null) =>
            new XsollaIdAuth(
                config ?? TestConfig.Valid(), _discovery, _browser, _tokens, store ?? _store,
                () => _now, () => pageUrl);

        // profileFetched defaults to true: the stored session has "already fetched
        // the profile", and all restore tests are written for that case. Migration
        // tests pass false explicitly.
        private void StoreSession(
            long expiresAtUnixMs, bool profileFetched = true, bool withAccess = true) => _store.Save(
            new XsollaIdSession(
                new XsollaUserId("sub-stored"), "Stored Name",
                withAccess ? new AccessToken("access-stored") : AccessToken.None,
                new RefreshToken("refresh-stored"),
                expiresAtUnixMs,
                AvatarUrl.None,
                profileFetched));

        private void UserInfoWithProfile(bool withPicture) => _tokens.UserInfoResult = UserInfoResponse.Ok(
            new XsollaUserId("sub-1"), "Player One",
            withPicture ? AvatarUrl.TryParse(PictureUrl) : AvatarUrl.None,
            userName: "id12345678", fullName: "Editor User");

        // ---- sign-in ----

        [TestCase(true)]
        [TestCase(false)]
        public async Task SignIn_Success(bool withPicture)
        {
            UserInfoWithProfile(withPicture);

            XsollaIdAuth auth = Create();
            int signedIn = 0;
            auth.SignedIn += _ => signedIn++;

            var result = await auth.SignInAsync() as XsollaIdResult.Success;

            Assert.IsNotNull(result, "expected success");
            Assert.IsTrue(auth.IsSignedIn);
            Assert.AreEqual("sub-1", auth.Current.Subject.Value);
            Assert.AreEqual("Player One", auth.Current.DisplayName);
            Assert.AreEqual(1, signedIn);

            // Fetched, with or without a picture: no reason to ask again.
            Assert.IsTrue(auth.Current.ProfileFetched);
            Assert.AreEqual(withPicture, auth.Current.Picture.IsValid);

            XsollaIdSession saved = _store.Load();

            Assert.IsNotNull(saved, "the session must be saved");
            Assert.AreEqual(auth.Current.Picture, saved.Picture);
        }

        [Test]
        public async Task SignIn_AuthorizeUrl_HasPkceScopesAndClient()
        {
            await Create().SignInAsync();

            string url = _browser.LastUrl;

            Assert.IsNotEmpty(_tokens.LastVerifier, "the verifier must be sent with the code exchange");
            StringAssert.Contains("response_type=code", url);
            StringAssert.Contains("code_challenge_method=S256", url);
            StringAssert.Contains("code_challenge=" + Pkce.CreateChallengeS256(_tokens.LastVerifier), url);
            StringAssert.Contains("client_id=client-stage", url);

            // offline, not offline_access: see XsollaIdConfig.DefaultScopes.
            StringAssert.Contains("offline", url);
            StringAssert.DoesNotContain("offline_access", url);
        }

        [Test]
        public async Task SignIn_Cancelled_ReturnsCancelledAndDoesNotExchange()
        {
            _browser.Result = BrowserAuthResult.Failed(XsollaIdErrorCode.Cancelled, "closed by the player");

            XsollaIdAuth auth = Create();
            int signedIn = 0;
            auth.SignedIn += _ => signedIn++;

            var failure = await auth.SignInAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.Cancelled, failure.Code);
            Assert.AreEqual(0, _tokens.ExchangeCalls);
            Assert.AreEqual(0, signedIn);
            Assert.IsFalse(auth.IsSignedIn);
        }

        [Test]
        public async Task SignIn_StateMismatch_FailsWithoutSpendingTheCode()
        {
            _browser.StateOverride = "state-tampered";

            var failure = await Create().SignInAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.InvalidState, failure.Code);
            Assert.AreEqual(0, _tokens.ExchangeCalls, "a one-time code must not be spent when CSRF is suspected");
        }

        [Test]
        public async Task SignIn_EmptyCode_FailsWithoutExchange()
        {
            _browser.Code = "";

            var failure = await Create().SignInAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.TokenExchangeFailed, failure.Code);
            Assert.AreEqual(0, _tokens.ExchangeCalls);
        }

        [Test]
        public async Task SignIn_DiscoveryReturnsNull_IsNotConfigured()
        {
            _discovery.Configuration = null;

            XsollaIdAuth auth = Create();

            var failure = await auth.SignInAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.NotConfigured, failure.Code);
            Assert.AreEqual(0, _browser.Calls, "there is no authorize endpoint to open");
            Assert.IsFalse(auth.IsSignedIn);
        }

        [Test]
        public async Task SignIn_ConcurrentCalls_OpenOnlyOneWindow()
        {
            _browser.Gate = new TaskCompletionSource<BrowserAuthResult>();

            XsollaIdAuth auth = Create();

            Task<XsollaIdResult> first = auth.SignInAsync();
            Task<XsollaIdResult> second = auth.SignInAsync();

            Assert.AreSame(first, second, "a second click must get the same task");
            Assert.AreEqual(1, _browser.Calls, "there must be no second window");

            _browser.Gate.SetResult(BrowserAuthResult.Ok("auth-code", _browser.LastExpectedState));

            Assert.IsInstanceOf<XsollaIdResult.Success>(await first);
        }

        // Single-flight must not latch: once the first sign-in is over, a new
        // click opens a new window.
        [Test]
        public async Task SignIn_AfterThePreviousCompleted_StartsANewOne()
        {
            XsollaIdAuth auth = Create();

            Task<XsollaIdResult> first = auth.SignInAsync();
            await first;
            await auth.SignOutAsync();

            Task<XsollaIdResult> second = auth.SignInAsync();

            Assert.AreNotSame(first, second);
            Assert.IsInstanceOf<XsollaIdResult.Success>(await second);
            Assert.AreEqual(2, _browser.Calls);
        }

        [Test]
        public async Task SignIn_ExchangeFailure_IsReportedAndNothingSaved()
        {
            _tokens.ExchangeResult =
                TokenResponse.Failed(XsollaIdErrorCode.TokenExchangeFailed, "invalid_grant");

            XsollaIdAuth auth = Create();

            var failure = await auth.SignInAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.TokenExchangeFailed, failure.Code);
            Assert.IsFalse(auth.IsSignedIn);
            Assert.IsNull(_store.Load());
        }

        // XsollaIdAuth throws nothing outward, even if a seam breaks its contract.
        [Test]
        public async Task SignIn_TokenClientThrows_ReturnsNetworkFailure()
        {
            _tokens.ExchangeThrows = true;

            XsollaIdAuth auth = Create();

            var failure = await auth.SignInAsync() as XsollaIdResult.Failure;

            Assert.IsNotNull(failure, "the exception must become a Failure, not escape");
            Assert.AreEqual(XsollaIdErrorCode.Network, failure.Code);
            Assert.IsFalse(auth.IsSignedIn);
        }

        // Userinfo is only for display: its failure must not cost the sign-in.
        [Test]
        public async Task SignIn_UserInfoFailure_StillSignsInWithoutProfile()
        {
            _tokens.UserInfoResult = UserInfoResponse.Failed(XsollaIdErrorCode.Network, "offline");

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.SignInAsync());
            Assert.IsTrue(auth.IsSignedIn);
            Assert.AreEqual("access-1", auth.Current.Access.Value);
            Assert.IsFalse(auth.Current.ProfileFetched, "a failed userinfo must be retried, not remembered");
        }

        // ---- restore ----

        [Test]
        public async Task Restore_NoStoredSession_ReturnsExpiredAndBecomesReady()
        {
            XsollaIdAuth auth = Create();
            Assert.IsFalse(auth.IsReady);

            var failure = await auth.RestoreAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.SessionExpired, failure.Code);
            Assert.IsTrue(auth.IsReady);
            Assert.IsFalse(auth.IsSignedIn);
            Assert.AreEqual(0, _browser.Calls, "restore must not open a window");
        }

        // Discovery is still warmed up (see RestoreAsync), but no token or
        // userinfo request is made, and it is a sign-in, not an update.
        [Test]
        public async Task Restore_ValidSession_SignsInWithoutTokenCalls()
        {
            StoreSession(_now + 3_600_000);

            XsollaIdAuth auth = Create();

            int signedIn = 0;
            int sessionUpdated = 0;
            auth.SignedIn += _ => signedIn++;
            auth.SessionUpdated += _ => sessionUpdated++;

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.IsTrue(auth.IsSignedIn);
            Assert.AreEqual("sub-stored", auth.Current.Subject.Value);
            Assert.AreEqual(1, signedIn);
            Assert.AreEqual(0, sessionUpdated);

            Assert.AreEqual(0, _tokens.RefreshCalls);
            Assert.AreEqual(0, _tokens.ExchangeCalls);
            Assert.AreEqual(0, _tokens.UserInfoCalls, "the profile was already fetched");
            Assert.AreEqual(1, _discovery.Calls, "discovery is warmed for a later sign-in click");
        }

        [Test]
        public async Task Restore_ExpiredSession_RefreshesExactlyOnce()
        {
            // Within the five-minute margin: treated as expired.
            StoreSession(_now + 60_000);

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.AreEqual(1, _tokens.RefreshCalls);
            Assert.AreEqual("access-2", auth.Current.Access.Value);
            // The identity is already known from the stored session; userinfo is not needed.
            Assert.AreEqual(0, _tokens.UserInfoCalls);
            Assert.AreEqual("Stored Name", auth.Current.DisplayName);
        }

        [Test]
        public async Task Restore_RefreshRejected_ClearsStoreAndStaysGuest()
        {
            StoreSession(_now - 1);
            _tokens.RefreshResult = TokenResponse.Failed(XsollaIdErrorCode.SessionExpired, "invalid_grant");

            XsollaIdAuth auth = Create();

            var failure = await auth.RestoreAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.SessionExpired, failure.Code);
            Assert.IsFalse(auth.IsSignedIn);
            Assert.IsNull(_store.Load(), "there is no point keeping a dead refresh token");
        }

        // The PlayerPrefs store already drops such a blob, so an in-memory store
        // is used to reach XsollaIdAuth's own !CanRefresh branch.
        [Test]
        public async Task Restore_ExpiredWithoutRefreshToken_ClearsStore()
        {
            var store = new FakeSessionStore
            {
                Session = new XsollaIdSession(
                    new XsollaUserId("sub-stored"), "Stored Name",
                    new AccessToken("access-stored"), RefreshToken.None, _now - 1)
            };

            XsollaIdAuth auth = Create(store: store);

            var failure = await auth.RestoreAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.SessionExpired, failure.Code);
            Assert.AreEqual(1, store.ClearCalls);
            Assert.IsNull(store.Session);
            Assert.AreEqual(0, _tokens.RefreshCalls, "there is nothing to refresh with");
            Assert.IsFalse(auth.IsSignedIn);
        }

        // ---- profile top-up ----

        // Migration: the blob was written by a version without pictures, so it has
        // no flag, while the token is still valid; the next refresh (where userinfo
        // happens anyway) may be an hour away.
        [Test]
        public async Task Restore_UnfetchedProfile_TopsUpExactlyOnceAndRewritesTheBlob()
        {
            StoreSession(_now + 3_600_000, profileFetched: false);
            _tokens.UserInfoResult = UserInfoResponse.Ok(
                new XsollaUserId("sub-stored"), "Fresh Name", AvatarUrl.TryParse(PictureUrl));

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.AreEqual(1, _tokens.UserInfoCalls);
            Assert.AreEqual(PictureUrl, auth.Current.Picture.Value);
            Assert.AreEqual(0, _tokens.RefreshCalls, "the token was still valid — no need to refresh it");

            XsollaIdSession saved = _store.Load();

            Assert.IsTrue(saved.ProfileFetched, "the blob must be rewritten so the top-up happens only once");
            Assert.AreEqual(PictureUrl, saved.Picture.Value);
        }

        [Test]
        public async Task Restore_TopUpFailure_StillSignsInAndRetriesLater()
        {
            StoreSession(_now + 3_600_000, profileFetched: false);
            _tokens.UserInfoResult = UserInfoResponse.Failed(XsollaIdErrorCode.Network, "offline");

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.IsTrue(auth.IsSignedIn, "a missing avatar must never cost the player the session");
            Assert.AreEqual("Stored Name", auth.Current.DisplayName, "the stored name must survive the failure");

            // Do not mark as fetched: a one-off network failure must not cost the
            // player the picture forever.
            Assert.IsFalse(_store.Load().ProfileFetched);
        }

        // The store accepts a blob without an access token, but such a session
        // never reaches the top-up request: IsExpired treats a missing token as
        // expired, so it goes to refresh, where userinfo is called normally with
        // the new token.
        [Test]
        public async Task Restore_WithoutAccessToken_RefreshesInsteadOfToppingUp()
        {
            StoreSession(_now + 3_600_000, profileFetched: false, withAccess: false);
            _tokens.UserInfoResult = UserInfoResponse.Ok(
                new XsollaUserId("sub-stored"), "Fresh Name", AvatarUrl.TryParse(PictureUrl));

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.AreEqual(1, _tokens.RefreshCalls, "a session with no access token has to be refreshed");
            Assert.AreEqual("access-2", auth.Current.Access.Value);

            // Exactly one request: the refresh and the profile top-up must not add
            // up to two.
            Assert.AreEqual(1, _tokens.UserInfoCalls);
            Assert.AreEqual(PictureUrl, auth.Current.Picture.Value);
        }

        // ---- refresh ----

        // Guards the condition in BuildSessionAsync: if "no picture" triggered the
        // request, a player without a picture claim would hit userinfo on every
        // token refresh, forever. Name, picture and both raw claims carry over.
        [TestCase(true)]
        [TestCase(false)]
        public async Task Refresh_KeepsProfileWithoutUserInfo(bool withPicture)
        {
            UserInfoWithProfile(withPicture);

            XsollaIdAuth auth = Create();
            await auth.SignInAsync();
            XsollaIdSession before = auth.Current;

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RefreshAsync());

            Assert.AreEqual("access-2", auth.Current.Access.Value, "the token really did roll over");
            Assert.AreEqual(1, _tokens.UserInfoCalls, "the profile was already fetched — asking again is a bug");
            Assert.AreEqual(before.Picture, auth.Current.Picture);
            Assert.AreEqual(withPicture, auth.Current.Picture.IsValid);
            Assert.AreEqual("Player One", auth.Current.DisplayName);
            Assert.AreEqual("id12345678", auth.Current.UserName);
            Assert.AreEqual("Editor User", auth.Current.FullName);
            Assert.IsTrue(auth.Current.ProfileFetched);
        }

        // The server may not rotate the refresh token; the old one stays usable.
        [Test]
        public async Task Refresh_WithoutNewRefreshToken_KeepsThePreviousOne()
        {
            _tokens.RefreshResult = TokenResponse.Ok(new AccessToken("access-2"), RefreshToken.None, 0);

            XsollaIdAuth auth = Create();
            await auth.SignInAsync();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RefreshAsync());
            Assert.AreEqual("access-2", auth.Current.Access.Value);
            Assert.AreEqual("refresh-1", auth.Current.Refresh.Value);
            Assert.AreEqual("refresh-1", _store.Load().Refresh.Value);
        }

        // A network blip is not a sign-out: the refresh token may still be good.
        [Test]
        public async Task Refresh_NetworkFailure_KeepsSessionAndStore()
        {
            XsollaIdAuth auth = Create();
            await auth.SignInAsync();
            XsollaIdSession before = auth.Current;

            int signedOut = 0;
            auth.SignedOut += () => signedOut++;
            _tokens.RefreshResult = TokenResponse.Failed(XsollaIdErrorCode.Network, "offline");

            var failure = await auth.RefreshAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.Network, failure.Code);
            Assert.AreSame(before, auth.Current);
            Assert.AreEqual("refresh-1", _store.Load().Refresh.Value);
            Assert.AreEqual(0, signedOut);
        }

        [Test]
        public async Task Refresh_SessionExpired_SignsOutAndClearsStore()
        {
            XsollaIdAuth auth = Create();
            await auth.SignInAsync();

            int signedOut = 0;
            auth.SignedOut += () => signedOut++;
            _tokens.RefreshResult = TokenResponse.Failed(XsollaIdErrorCode.SessionExpired, "invalid_grant");

            var failure = await auth.RefreshAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.SessionExpired, failure.Code);
            Assert.IsFalse(auth.IsSignedIn);
            Assert.IsNull(_store.Load());
            Assert.AreEqual(1, signedOut);
        }

        [Test]
        public async Task Refresh_RaisesSessionUpdatedAndNotSignedIn()
        {
            XsollaIdAuth auth = Create();

            int signedIn = 0;
            int sessionUpdated = 0;
            auth.SignedIn += _ => signedIn++;
            auth.SessionUpdated += _ => sessionUpdated++;

            await auth.SignInAsync();

            Assert.AreEqual(1, signedIn);
            Assert.AreEqual(0, sessionUpdated, "the first sign-in is SignedIn, not an update");

            await auth.RefreshAsync();

            Assert.AreEqual(1, signedIn, "a token refresh is not a new sign-in");
            Assert.AreEqual(1, sessionUpdated);
        }

        // ---- LastSignInOrigin ----

        // One instance walks through all three origins, so a stale value from the
        // previous step would be caught (Restored is also the initial value).
        [Test]
        public async Task LastSignInOrigin_FollowsWhereTheSessionCameFrom()
        {
            XsollaIdAuth auth = Create();

            await auth.SignInAsync();
            Assert.AreEqual(XsollaIdSignInOrigin.Interactive, auth.LastSignInOrigin);

            await auth.RefreshAsync();
            Assert.AreEqual(XsollaIdSignInOrigin.Refreshed, auth.LastSignInOrigin);

            await auth.SignInAsync();
            Assert.AreEqual(XsollaIdSignInOrigin.Interactive, auth.LastSignInOrigin);

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.AreEqual(XsollaIdSignInOrigin.Restored, auth.LastSignInOrigin);
        }

        // ---- sign-out ----

        [Test]
        public async Task SignOut_ClearsEverythingAndRaisesEvent()
        {
            XsollaIdAuth auth = Create();
            await auth.SignInAsync();

            int signedOut = 0;
            auth.SignedOut += () => signedOut++;

            await auth.SignOutAsync();

            Assert.IsFalse(auth.IsSignedIn);
            Assert.IsNull(auth.Current);
            Assert.IsNull(_store.Load());
            Assert.AreEqual(1, signedOut);
            Assert.AreEqual(1, _tokens.RevokeCalls);
        }

        [Test]
        public async Task SignOut_WhenNotSignedIn_RaisesNothing()
        {
            XsollaIdAuth auth = Create();

            int signedOut = 0;
            auth.SignedOut += () => signedOut++;

            await auth.SignOutAsync();

            Assert.AreEqual(0, signedOut, "SignedOut means a transition, and there was none");
            Assert.AreEqual(0, _tokens.RevokeCalls);
        }

        [Test]
        public async Task SignOut_SurvivesRevokeFailure()
        {
            // The Xsolla ID revocation endpoint has returned 405 before; signing out
            // must not depend on it.
            _tokens.RevokeThrows = true;

            XsollaIdAuth auth = Create();
            await auth.SignInAsync();

            Assert.DoesNotThrowAsync(async () => await auth.SignOutAsync());
            Assert.IsFalse(auth.IsSignedIn);
            Assert.IsNull(_store.Load());
        }

        // ---- unavailability ----

        [Test]
        public async Task Unavailable_WhenBrowserUnsupported()
        {
            _browser.IsSupported = false;

            XsollaIdAuth auth = Create();

            Assert.IsFalse(auth.IsAvailable);
            Assert.AreEqual(
                XsollaIdErrorCode.Unavailable, ((XsollaIdResult.Failure)await auth.SignInAsync()).Code);
            Assert.AreEqual(0, _browser.Calls);
        }

        [Test]
        public async Task Unavailable_WhenConfigIncomplete()
        {
            var broken = new XsollaIdConfig(
                XsollaIdEnvironment.Stage,
                new XsollaIdClientRegistration(XsollaIdConfig.DefaultStageIssuer, null, "http://localhost:8081/cb.html"),
                null);

            XsollaIdAuth auth = Create(broken);

            Assert.IsFalse(auth.IsAvailable);
            Assert.AreEqual(
                XsollaIdErrorCode.Unavailable, ((XsollaIdResult.Failure)await auth.RestoreAsync()).Code);
            Assert.IsTrue(auth.IsReady, "unavailable is a final answer too");
        }

        // Only 127.0.0.1 is allowed: localhost must fail before any window or
        // discovery request, since the server would refuse anyway.
        [TestCase("http://localhost:8081/index.html", false)]
        [TestCase("http://127.0.0.1:8081/index.html", true)]
        public async Task SignIn_Origin(string pageUrl, bool proceeds)
        {
            XsollaIdResult result = await Create(pageUrl: pageUrl).SignInAsync();

            if (proceeds)
            {
                Assert.IsInstanceOf<XsollaIdResult.Success>(result);
                return;
            }

            Assert.AreEqual(XsollaIdErrorCode.NotConfigured, ((XsollaIdResult.Failure)result).Code);
            Assert.AreEqual(0, _browser.Calls);
            Assert.AreEqual(0, _discovery.Calls);
        }
    }
}
