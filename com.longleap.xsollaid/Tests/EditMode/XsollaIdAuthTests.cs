using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class XsollaIdAuthTests
    {
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
        private XsollaIdAuth Create(XsollaIdConfig config = null, string pageUrl = "") => new XsollaIdAuth(
            config ?? TestConfig.Valid(), _discovery, _browser, _tokens, _store, () => _now, () => pageUrl);

        // profileFetched defaults to true: the stored session has "already fetched
        // the profile", and all restore tests are written for that case. Migration
        // tests pass false explicitly.
        private void StoreSession(
            long expiresAtUnixMs, bool withRefresh = true, bool profileFetched = true,
            string picture = null, bool withAccess = true) => _store.Save(
            new XsollaIdSession(
                new XsollaUserId("sub-stored"), "Stored Name",
                withAccess ? new AccessToken("access-stored") : AccessToken.None,
                withRefresh ? new RefreshToken("refresh-stored") : RefreshToken.None,
                expiresAtUnixMs,
                AvatarUrl.TryParse(picture),
                profileFetched));

        // ---- sign-in ----

        [Test]
        public async Task SignIn_Success_SetsSessionSavesAndRaisesEventOnce()
        {
            XsollaIdAuth auth = Create();
            int signedIn = 0;
            auth.SignedIn += _ => signedIn++;

            var result = await auth.SignInAsync() as XsollaIdResult.Success;

            Assert.IsNotNull(result, "expected success");
            Assert.IsTrue(auth.IsSignedIn);
            Assert.AreEqual("sub-1", auth.Current.Subject.Value);
            Assert.AreEqual("Player One", auth.Current.DisplayName);
            Assert.AreEqual(1, signedIn);
            Assert.IsNotNull(_store.Load(), "the session must be saved");
        }

        [Test]
        public async Task SignIn_SendsPkceVerifierAndS256Challenge()
        {
            XsollaIdAuth auth = Create();

            await auth.SignInAsync();

            Assert.IsNotEmpty(_tokens.LastVerifier, "the verifier must be sent with the code exchange");
            StringAssert.Contains("code_challenge_method=S256", _browser.LastUrl);
            StringAssert.Contains(
                "code_challenge=" + Pkce.CreateChallengeS256(_tokens.LastVerifier), _browser.LastUrl);
            StringAssert.Contains("response_type=code", _browser.LastUrl);
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

        [Test]
        public async Task Restore_ValidSession_SignsInWithoutAnyNetworkCall()
        {
            StoreSession(_now + 3_600_000);

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.IsTrue(auth.IsSignedIn);
            Assert.AreEqual("sub-stored", auth.Current.Subject.Value);
            Assert.AreEqual(0, _tokens.RefreshCalls);
            Assert.AreEqual(0, _tokens.ExchangeCalls);

            // The test name means exactly what it says: the profile was already
            // fetched, so there must be no top-up userinfo call.
            Assert.AreEqual(0, _tokens.UserInfoCalls);
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

        [Test]
        public async Task Restore_ExpiredWithoutRefreshToken_ClearsStore()
        {
            StoreSession(_now - 1, withRefresh: false);

            // Without a refresh token the stored session is not loaded at all:
            // the store returns null and clears the key.
            XsollaIdAuth auth = Create();

            var failure = await auth.RestoreAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.SessionExpired, failure.Code);
            Assert.AreEqual(0, _tokens.RefreshCalls);
        }

        // ---- profile picture and top-up request ----

        [Test]
        public async Task SignIn_CarriesThePictureIntoTheSession()
        {
            _tokens.UserInfoResult = UserInfoResponse.Ok(
                new XsollaUserId("sub-1"), "Player One", AvatarUrl.TryParse("https://cdn.xsolla.com/a.png"));

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.SignInAsync());
            Assert.AreEqual("https://cdn.xsolla.com/a.png", auth.Current.Picture.Value);
            Assert.IsTrue(auth.Current.ProfileFetched);
            Assert.AreEqual("https://cdn.xsolla.com/a.png", _store.Load().Picture.Value);
        }

        [Test]
        public async Task SignIn_WithoutPicture_StillSucceeds()
        {
            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.SignInAsync());
            Assert.IsFalse(auth.Current.Picture.IsValid);

            // Fetched and got nothing: no reason to ask again.
            Assert.IsTrue(auth.Current.ProfileFetched);
        }

        // The MOST IMPORTANT test in this group: it catches a naive fix of the
        // condition in BuildSessionAsync. If "no picture" were the trigger to ask,
        // a player without a picture claim would hit userinfo on EVERY token
        // refresh, forever.
        [Test]
        public async Task Refresh_DoesNotAskUserInfoAgain_EvenWithoutAPicture()
        {
            XsollaIdAuth auth = Create();
            await auth.SignInAsync();

            Assert.AreEqual(1, _tokens.UserInfoCalls);
            Assert.IsFalse(auth.Current.Picture.IsValid, "this test is only meaningful without a picture");

            await auth.RefreshAsync();

            Assert.AreEqual(1, _tokens.UserInfoCalls, "the profile was already fetched — asking again is a bug");
        }

        [Test]
        public async Task Refresh_CarriesThePictureForward()
        {
            _tokens.UserInfoResult = UserInfoResponse.Ok(
                new XsollaUserId("sub-1"), "Player One", AvatarUrl.TryParse("https://cdn.xsolla.com/a.png"));

            XsollaIdAuth auth = Create();
            await auth.SignInAsync();
            await auth.RefreshAsync();

            Assert.AreEqual("https://cdn.xsolla.com/a.png", auth.Current.Picture.Value);
            Assert.AreEqual("access-2", auth.Current.Access.Value, "the token really did roll over");
            Assert.AreEqual(1, _tokens.UserInfoCalls);
        }

        // Migration: the blob was written by a version without pictures, so it has
        // no flag, while the token is still valid; the next refresh (where userinfo
        // happens anyway) may be an hour away.
        [Test]
        public async Task Restore_UnfetchedProfile_TopsUpExactlyOnceAndRewritesTheBlob()
        {
            StoreSession(_now + 3_600_000, profileFetched: false);
            _tokens.UserInfoResult = UserInfoResponse.Ok(
                new XsollaUserId("sub-stored"), "Fresh Name", AvatarUrl.TryParse("https://cdn.xsolla.com/a.png"));

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.AreEqual(1, _tokens.UserInfoCalls);
            Assert.AreEqual("https://cdn.xsolla.com/a.png", auth.Current.Picture.Value);
            Assert.AreEqual(0, _tokens.RefreshCalls, "the token was still valid — no need to refresh it");

            XsollaIdSession saved = _store.Load();

            Assert.IsTrue(saved.ProfileFetched, "the blob must be rewritten so the top-up happens only once");
            Assert.AreEqual("https://cdn.xsolla.com/a.png", saved.Picture.Value);
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
                new XsollaUserId("sub-stored"), "Fresh Name", AvatarUrl.TryParse("https://cdn.xsolla.com/a.png"));

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.AreEqual(1, _tokens.RefreshCalls, "a session with no access token has to be refreshed");
            Assert.AreEqual("access-2", auth.Current.Access.Value);

            // Exactly one request: the refresh and the profile top-up must not add
            // up to two.
            Assert.AreEqual(1, _tokens.UserInfoCalls);
            Assert.AreEqual("https://cdn.xsolla.com/a.png", auth.Current.Picture.Value);
        }

        // ---- SessionUpdated ----

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

        [Test]
        public async Task Restore_OfAValidSession_RaisesSignedInNotSessionUpdated()
        {
            StoreSession(_now + 3_600_000);

            XsollaIdAuth auth = Create();

            int signedIn = 0;
            int sessionUpdated = 0;
            auth.SignedIn += _ => signedIn++;
            auth.SessionUpdated += _ => sessionUpdated++;

            await auth.RestoreAsync();

            Assert.AreEqual(1, signedIn);
            Assert.AreEqual(0, sessionUpdated);
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

        [Test]
        public async Task SignIn_DisallowedOrigin_FailsBeforeOpeningWindow()
        {
            // The game is opened on localhost, but only 127.0.0.1 is allowed.
            XsollaIdAuth auth = Create(pageUrl: "http://localhost:8081/index.html");

            var failure = await auth.SignInAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.NotConfigured, failure.Code);
            Assert.AreEqual(0, _browser.Calls, "no point opening the window — the server would refuse anyway");
            Assert.AreEqual(0, _discovery.Calls);
        }

        [Test]
        public async Task SignIn_AllowedOrigin_ProceedsNormally()
        {
            XsollaIdAuth auth = Create(pageUrl: "http://127.0.0.1:8081/index.html");

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.SignInAsync());
        }

        [Test]
        public void DefaultScopes_RequestOfflineForRefreshToken()
        {
            // offline, not offline_access: on this Hydra instance it is the scope
            // that issues a refresh token. Without it the player has to sign in again
            // after every reload.
            CollectionAssert.Contains(XsollaIdConfig.DefaultScopes.Split(' '), "offline");
            StringAssert.DoesNotContain("offline_access", XsollaIdConfig.DefaultScopes);
        }

        [Test]
        public async Task SignIn_AuthorizeUrlCarriesConfiguredScopes()
        {
            await Create().SignInAsync();

            StringAssert.Contains("offline", _browser.LastUrl);
            StringAssert.DoesNotContain("offline_access", _browser.LastUrl);
            StringAssert.Contains("client_id=client-stage", _browser.LastUrl);
        }

        [Test]
        public async Task FirebaseAuthKeyIsNeverTouched()
        {
            _prefs.SetString("longleap_auth", "firebase-blob");

            XsollaIdAuth auth = Create();
            await auth.SignInAsync();
            await auth.RefreshAsync();
            await auth.SignOutAsync();

            Assert.AreEqual("firebase-blob", _prefs.GetString("longleap_auth"),
                "the anonymous Firebase identity holds the saves and must not be touched");
        }
    }
}
