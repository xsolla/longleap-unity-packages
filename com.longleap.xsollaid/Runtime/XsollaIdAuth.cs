using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.XsollaId
{
    // Машина состояний входа через Xsolla ID.
    //
    // Все внешние зависимости — швы, поэтому весь класс проверяется
    // EditMode-тестами без браузера, без сети и без зарегистрированного
    // clientId.
    //
    // Наружу не бросает НИЧЕГО: любой сбой возвращается XsollaIdResult.Failure
    // с кодом. Исключение, всплывшее в Task, который опрашивают из корутины
    // бутстрапа, — это повисшая загрузка.
    public sealed class XsollaIdAuth : IXsollaIdAuth
    {
        // Тот же запас, что у FirebaseAnonymousAuthProvider: не отдаём токен,
        // которому осталось меньше пяти минут, чтобы он не протух по дороге.
        private const long TokenRefreshMarginMs = 300_000;

        // Пять минут, а не три: первый вход часто идёт через MFA, и игроку
        // нужно время. Столько же отводит standalone-флоу Backpack Cat
        // (Confluence 24612667695, §3.1) — тот же IdP, та же MFA.
        private static readonly TimeSpan PopupTimeout = TimeSpan.FromMinutes(5);

        private readonly XsollaIdConfig _config;
        private readonly IOidcDiscovery _discovery;
        private readonly IXsollaIdBrowser _browser;
        private readonly IXsollaIdTokenClient _tokens;
        private readonly IXsollaIdSessionStore _store;
        private readonly Func<long> _nowUnixMs;
        private readonly Func<string> _pageUrl;

        // Обновление и восстановление сериализуем: два параллельных обновления
        // сожгут refresh token дважды.
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        // Для входа семафор НЕ подходит: он бы не отменил второй вызов, а
        // поставил его в очередь — и по двойному клику открылось бы второе
        // окно уже после закрытия первого. Поэтому держим саму задачу и
        // отдаём её повторно.
        private Task<XsollaIdResult> _signInInFlight;

        public XsollaIdAuth(
            XsollaIdConfig config,
            IOidcDiscovery discovery,
            IXsollaIdBrowser browser,
            IXsollaIdTokenClient tokens,
            IXsollaIdSessionStore store,
            Func<long> nowUnixMs = null,
            Func<string> pageUrl = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _browser = browser ?? throw new ArgumentNullException(nameof(browser));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _nowUnixMs = nowUnixMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _pageUrl = pageUrl ?? (() => Application.absoluteURL);
        }

        public bool IsAvailable => _browser.IsSupported && _config.IsValid;

        public bool IsReady { get; private set; }

        public bool IsSignedIn => Current != null;

        public XsollaIdSession Current { get; private set; }

        // По умолчанию НЕ Interactive: спросить игрока можно только там, где он
        // сам нажал кнопку, и случайное чтение до первого входа не имеет права
        // выглядеть как его жест.
        public XsollaIdSignInOrigin LastSignInOrigin { get; private set; } = XsollaIdSignInOrigin.Restored;

        public event Action<XsollaIdSession> SignedIn;
        public event Action SignedOut;
        public event Action<XsollaIdSession> SessionUpdated;

        public async Task<XsollaIdResult> RestoreAsync()
        {
            if (!IsAvailable)
            {
                IsReady = true;
                return XsollaIdResult.Unavailable();
            }

            await _refreshLock.WaitAsync();

            try
            {
                // Discovery прогреваем ВСЕГДА, даже если восстанавливать
                // нечего. Дальше SignInAsync возьмёт его из кэша, и ожидание
                // уже завершённой задачи продолжится синхронно — жест игрока
                // не потеряется и окно не заблокируют. Холодный кэш на клике —
                // это заблокированный popup.
                _ = WarmDiscoveryAsync();

                XsollaIdSession stored = _store.Load();

                if (stored == null)
                    return XsollaIdResult.Expired();

                if (!stored.IsExpired(_nowUnixMs(), TokenRefreshMarginMs))
                {
                    // Догоняем профиль ровно один раз — см. TopUpProfileAsync.
                    // Ждать здесь можно: бутстрап зовёт RestoreAsync и
                    // выбрасывает задачу, так что лишний round-trip ничего не
                    // задерживает.
                    XsollaIdSession adopted = stored.ProfileFetched ? stored : await TopUpProfileAsync(stored);

                    // Adopt получает УЖЕ дополненную сессию, поэтому SignedIn
                    // сразу несёт картинку и второго события подписчикам не
                    // нужно.
                    Adopt(adopted, XsollaIdSignInOrigin.Restored);

                    if (!ReferenceEquals(adopted, stored))
                        _store.Save(adopted);

                    return new XsollaIdResult.Success(adopted);
                }

                if (!stored.CanRefresh)
                {
                    _store.Clear();
                    return XsollaIdResult.Expired();
                }

                return await RefreshLockedAsync(stored, XsollaIdSignInOrigin.Restored);
            }
            catch (Exception ex)
            {
                return Fail(XsollaIdErrorCode.Network, $"session restore failed: {ex.Message}");
            }
            finally
            {
                IsReady = true;
                _refreshLock.Release();
            }
        }

        public async Task<XsollaIdResult> RefreshAsync()
        {
            if (!IsAvailable)
                return XsollaIdResult.Unavailable();

            await _refreshLock.WaitAsync();

            try
            {
                XsollaIdSession session = Current ?? _store.Load();

                if (session == null || !session.CanRefresh)
                    return XsollaIdResult.Expired();

                return await RefreshLockedAsync(session, XsollaIdSignInOrigin.Refreshed);
            }
            catch (Exception ex)
            {
                return Fail(XsollaIdErrorCode.Network, $"token refresh failed: {ex.Message}");
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public Task<XsollaIdResult> SignInAsync()
        {
            if (!IsAvailable)
                return Task.FromResult(XsollaIdResult.Unavailable());

            // Повторный клик по кнопке не должен открывать второе окно.
            if (_signInInFlight != null && !_signInInFlight.IsCompleted)
                return _signInInFlight;

            _signInInFlight = SignInInternalAsync();

            return _signInInFlight;
        }

        public async Task SignOutAsync()
        {
            XsollaIdSession session = Current;

            // Локальную часть делаем ПЕРВОЙ и безусловно: даже если отзыв не
            // пройдёт, игрок должен оказаться вышедшим.
            Current = null;
            _store.Clear();

            if (session != null)
                SignedOut?.Invoke();

            if (session == null || !session.Access.IsValid || !IsAvailable)
                return;

            try
            {
                await _tokens.RevokeAsync(session.Access.Value);
            }
            catch (Exception ex)
            {
                // Отзыв — best-effort: у Xsolla ID этот эндпоинт уже отдавал
                // 405, и выход из аккаунта не должен от него зависеть.
                Debug.LogWarning($"{nameof(XsollaIdAuth)}: token revocation failed — {ex.Message}");
            }
        }

        private async Task<XsollaIdResult> SignInInternalAsync()
        {
            try
            {
                XsollaIdClientRegistration client = _config.Active;

                // Проверяем ДО открытия окна: сервер сверяет redirect_uri
                // посимвольно, и его отказ выглядит невнятно. Заодно ловим
                // localhost вместо 127.0.0.1 — это разные origin'ы.
                if (!OriginGuard.IsAllowed(_pageUrl(), _config.AllowedGameOrigins, out string problem))
                    return Fail(XsollaIdErrorCode.NotConfigured, problem);

                OidcConfiguration oidc = await _discovery.GetAsync();

                if (oidc == null || !oidc.IsUsable)
                    return Fail(XsollaIdErrorCode.NotConfigured, "discovery did not return the authorize/token endpoints.");

                // Ровно то, что сейчас уйдёт на сервер. В логе нужна сама
                // строка, а не «адрес из конфига»: сверку redirect_uri сервер
                // делает посимвольно, и расхождение в схеме, порте или
                // завершающем слэше по ассету глазами не находится. Тот же
                // адрес потом идёт в обмен кода — если он не совпадёт,
                // отказ придёт уже там.
                Debug.Log($"{nameof(XsollaIdAuth)}: opening sign-in — page '{_pageUrl()}', " +
                    $"redirect_uri '{client.RedirectUri}', authorize endpoint '{oidc.AuthorizationEndpoint}'.");

                string verifier = Pkce.CreateVerifier();
                string state = Pkce.CreateState();
                string url = BuildAuthorizeUrl(
                    oidc, client, _config.Scopes, Pkce.CreateChallengeS256(verifier), state);

                BrowserAuthResult browser = await _browser.AuthorizeAsync(url, state, PopupTimeout);

                if (!browser.IsSuccess)
                    return new XsollaIdResult.Failure(browser.Error.Value, browser.Message);

                // Сверяем state ДО обмена: расхождение — возможная CSRF, и
                // тратить на неё одноразовый код нельзя.
                if (!string.Equals(browser.State, state, StringComparison.Ordinal))
                    return Fail(XsollaIdErrorCode.InvalidState, "the state in the response did not match the one sent.");

                if (string.IsNullOrEmpty(browser.Code))
                    return Fail(XsollaIdErrorCode.TokenExchangeFailed, "the server did not return an authorization code.");

                TokenResponse tokens =
                    await _tokens.ExchangeCodeAsync(browser.Code, verifier, client.RedirectUri);

                if (!tokens.IsSuccess)
                    return new XsollaIdResult.Failure(tokens.Error.Value, tokens.Message);

                XsollaIdSession session = await BuildSessionAsync(tokens, previous: null);

                Adopt(session, XsollaIdSignInOrigin.Interactive);
                _store.Save(session);

                return new XsollaIdResult.Success(session);
            }
            catch (Exception ex)
            {
                return Fail(XsollaIdErrorCode.Network, $"sign-in failed: {ex.Message}");
            }
        }

        // Вызывается только под _refreshLock.
        // origin передаётся насквозь, потому что обновление токена приходит из
        // двух разных мест: из восстановления сессии на старте (там это переход
        // «не вошёл → вошёл», и спрашивать игрока нельзя) и из явного
        // RefreshAsync у уже вошедшего.
        private async Task<XsollaIdResult> RefreshLockedAsync(
            XsollaIdSession session, XsollaIdSignInOrigin origin)
        {
            TokenResponse tokens = await _tokens.RefreshAsync(session.Refresh.Value);

            if (!tokens.IsSuccess)
            {
                // Refresh token живёт 720 часов. Его смерть — штатный исход,
                // а не сбой: чистим и молча уходим в гости.
                if (tokens.Error == XsollaIdErrorCode.SessionExpired)
                {
                    _store.Clear();
                    Forget();
                }

                return new XsollaIdResult.Failure(tokens.Error.Value, tokens.Message);
            }

            XsollaIdSession refreshed = await BuildSessionAsync(tokens, previous: session);

            Adopt(refreshed, origin);
            _store.Save(refreshed);

            return new XsollaIdResult.Success(refreshed);
        }

        // Профиль тянем, только если для этой личности его ещё не тянули:
        // userinfo — лишний round-trip, а при обновлении токена личность не
        // меняется.
        //
        // Признаком раньше служило непустое имя. С картинкой это перестало
        // работать: клейма picture может не быть вовсе, и условие «картинки нет
        // → сходи ещё раз» означало бы лишний userinfo на КАЖДОЕ обновление
        // токена, навсегда. Поэтому признак теперь отдельный и сохраняется
        // вместе с сессией.
        private async Task<XsollaIdSession> BuildSessionAsync(TokenResponse tokens, XsollaIdSession previous)
        {
            XsollaUserId subject = previous?.Subject ?? XsollaUserId.None;
            string displayName = previous?.DisplayName;
            AvatarUrl picture = previous?.Picture ?? AvatarUrl.None;
            bool profileFetched = previous?.ProfileFetched ?? false;

            // Переносим из прошлой сессии по той же причине, что имя и картинку:
            // при обновлении токена userinfo не случается, а личность та же.
            string userName = previous?.UserName;
            string fullName = previous?.FullName;

            if (!subject.IsValid || !profileFetched)
            {
                UserInfoResponse info = await _tokens.GetUserInfoAsync(tokens.Access.Value);

                if (info.IsSuccess)
                {
                    subject = info.Subject;
                    displayName = info.DisplayName;
                    picture = info.Picture;
                    userName = info.UserName;
                    fullName = info.FullName;

                    // Отмечаем ТОЛЬКО на успехе: отказ userinfo должен
                    // повториться на следующем обновлении, а не запомниться как
                    // «спрашивали».
                    profileFetched = true;
                }
                else
                {
                    // Без userinfo вход всё равно состоялся — просто показать
                    // будет нечего.
                    Debug.LogWarning($"{nameof(XsollaIdAuth)}: userinfo unavailable — {info.Message}");
                }
            }

            RefreshToken refresh = tokens.Refresh.IsValid
                ? tokens.Refresh
                : previous?.Refresh ?? RefreshToken.None;

            return new XsollaIdSession(
                subject, displayName, tokens.Access, refresh, tokens.ExpiresAtUnixMs, picture, profileFetched,
                userName, fullName);
        }

        // Догоняющий запрос профиля для сессии, поднятой из хранилища.
        //
        // Нужен потому, что RestoreAsync принимает ещё живую сессию НЕ через
        // BuildSessionAsync: у блоба, записанного до появления картинки, имя
        // есть, а картинки нет, и обновления токена (где userinfo и так
        // случается) можно не дождаться — access token живёт час, а вкладку
        // игрок может не закрывать сутками.
        //
        // Ничего не бросает и восстановление не проваливает: без картинки
        // сессия полностью рабочая, поэтому любой отказ — просто предупреждение.
        private async Task<XsollaIdSession> TopUpProfileAsync(XsollaIdSession session)
        {
            // Страховка: спрашивать было бы нечем. Сейчас сюда не попасть —
            // IsExpired считает отсутствие access token'а истечением, поэтому
            // такая сессия уходит на обновление и в BuildSessionAsync. Но
            // зависеть от чужого условия в другом файле незачем.
            if (!session.Access.IsValid)
                return session;

            UserInfoResponse info = await _tokens.GetUserInfoAsync(session.Access.Value);

            if (!info.IsSuccess)
            {
                Debug.LogWarning($"{nameof(XsollaIdAuth)}: userinfo unavailable — {info.Message}");
                return session;
            }

            return new XsollaIdSession(
                info.Subject.IsValid ? info.Subject : session.Subject,
                string.IsNullOrEmpty(info.DisplayName) ? session.DisplayName : info.DisplayName,
                session.Access,
                session.Refresh,
                session.ExpiresAtUnixMs,
                info.Picture,
                profileFetched: true,

                // Пустой клейм не затирает то, что уже лежало в сессии: у
                // догоняющего запроса задача дополнить, а не обеднить.
                userName: string.IsNullOrEmpty(info.UserName) ? session.UserName : info.UserName,
                fullName: string.IsNullOrEmpty(info.FullName) ? session.FullName : info.FullName);
        }

        private static string BuildAuthorizeUrl(
            OidcConfiguration oidc, XsollaIdClientRegistration client, string scopes, string challenge, string state)
        {
            return $"{oidc.AuthorizationEndpoint}" +
                   $"?response_type=code" +
                   $"&client_id={Uri.EscapeDataString(client.ClientId)}" +
                   $"&redirect_uri={Uri.EscapeDataString(client.RedirectUri)}" +
                   $"&scope={Uri.EscapeDataString(scopes)}" +
                   $"&state={Uri.EscapeDataString(state)}" +
                   $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                   $"&code_challenge_method=S256";
        }

        private async Task WarmDiscoveryAsync()
        {
            try
            {
                await _discovery.GetAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaIdAuth)}: discovery warm-up failed — {ex.Message}");
            }
        }

        private void Adopt(XsollaIdSession session, XsollaIdSignInOrigin origin)
        {
            bool wasSignedOut = Current == null;

            Current = session;

            // ДО событий: подписчик SignedIn читает признак сразу в
            // обработчике.
            LastSignInOrigin = origin;

            // SignedIn — только на переходе «не вошёл → вошёл»: на него
            // подписчики полагаются именно в этом смысле.
            //
            // Но Current при обновлении токена подменяется НОВЫМ объектом, и
            // молчать об этом нельзя: подписчик, запомнивший поля из полезной
            // нагрузки SignedIn, тихо остался бы со старой картинкой и старым
            // именем. Поэтому у смены сессии без смены игрока своё событие.
            if (wasSignedOut)
                SignedIn?.Invoke(session);
            else
                SessionUpdated?.Invoke(session);
        }

        private void Forget()
        {
            if (Current == null)
                return;

            Current = null;
            SignedOut?.Invoke();
        }

        private static XsollaIdResult Fail(XsollaIdErrorCode code, string message)
        {
            Debug.LogWarning($"{nameof(XsollaIdAuth)}: {message}");

            return new XsollaIdResult.Failure(code, message);
        }
    }
}
