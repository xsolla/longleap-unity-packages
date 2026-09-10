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

        // Управляемые часы: тесты про истечение токена не должны зависеть от
        // реального времени.
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

        // pageUrl пустой — как в редакторе: проверку origin'а это проходит,
        // и тесты не зависят от того, где «открыта» игра. Отдельно её
        // поведение проверяет OriginGuardTests.
        private XsollaIdAuth Create(XsollaIdConfig config = null, string pageUrl = "") => new XsollaIdAuth(
            config ?? TestConfig.Valid(), _discovery, _browser, _tokens, _store, () => _now, () => pageUrl);

        // profileFetched по умолчанию true: сохранённая сессия «уже спрашивала
        // профиль», и все тесты про восстановление написаны именно про этот
        // случай. Миграционные тесты передают false явно.
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

        // ---- вход ----

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

        // ---- восстановление ----

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

            // Теперь имя теста означает буквально то, что написано: профиль уже
            // спрашивали, и догоняющего userinfo быть не должно.
            Assert.AreEqual(0, _tokens.UserInfoCalls);
        }

        [Test]
        public async Task Restore_ExpiredSession_RefreshesExactlyOnce()
        {
            // Внутри пятиминутного запаса — считается истёкшим.
            StoreSession(_now + 60_000);

            XsollaIdAuth auth = Create();

            Assert.IsInstanceOf<XsollaIdResult.Success>(await auth.RestoreAsync());
            Assert.AreEqual(1, _tokens.RefreshCalls);
            Assert.AreEqual("access-2", auth.Current.Access.Value);
            // Личность уже известна из сохранённой сессии — userinfo не нужен.
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

            // Без refresh token'а сохранённая сессия вообще не читается —
            // хранилище отдаёт null и чистит ключ.
            XsollaIdAuth auth = Create();

            var failure = await auth.RestoreAsync() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.SessionExpired, failure.Code);
            Assert.AreEqual(0, _tokens.RefreshCalls);
        }

        // ---- картинка профиля и догоняющий запрос ----

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

            // Спрашивали и не получили — второй раз ходить незачем.
            Assert.IsTrue(auth.Current.ProfileFetched);
        }

        // САМЫЙ ВАЖНЫЙ тест этой пачки: ловит наивную починку условия в
        // BuildSessionAsync. Если признаком «надо спросить» сделать «картинки
        // нет», то у игрока без клейма picture userinfo будет дёргаться на
        // КАЖДОЕ обновление токена — навсегда.
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

        // Миграция: блоб записан версией без картинки, поэтому признака в нём
        // нет, а токен ещё живой — обновления (где userinfo и так случается)
        // можно ждать час.
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

            // Не отмечаем «спрашивали»: разовый сбой сети не должен навсегда
            // лишить игрока картинки.
            Assert.IsFalse(_store.Load().ProfileFetched);
        }

        // Блоб без access token'а хранилище допускает, и до догоняющего запроса
        // такая сессия НЕ доходит: IsExpired считает отсутствие токена
        // истечением, поэтому она уходит на обновление. Там userinfo случается
        // штатно — уже с новым токеном.
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

            // Ровно один запрос: обновление и догоняющий профиль не должны
            // сложиться в два.
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

        // ---- выход ----

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
            // Эндпоинт отзыва у Xsolla ID уже отдавал 405 — выход из аккаунта
            // не должен от него зависеть.
            _tokens.RevokeThrows = true;

            XsollaIdAuth auth = Create();
            await auth.SignInAsync();

            Assert.DoesNotThrowAsync(async () => await auth.SignOutAsync());
            Assert.IsFalse(auth.IsSignedIn);
            Assert.IsNull(_store.Load());
        }

        // ---- недоступность ----

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
            // Игра открыта на localhost, а разрешён только 127.0.0.1.
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
            // Именно offline, а не offline_access: на этом инстансе Hydra
            // refresh token выдаёт он (Confluence 24612667695). Без него игрок
            // будет входить заново после каждой перезагрузки.
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
