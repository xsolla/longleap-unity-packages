using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.XsollaId
{
    // Xsolla ID sign-in state machine.
    //
    // Every external dependency is a seam, so the whole class is covered by
    // EditMode tests without a browser, without network and without a
    // registered clientId.
    //
    // Throws NOTHING outward: every failure is returned as
    // XsollaIdResult.Failure with a code. An exception surfacing in a Task that
    // a bootstrap coroutine polls means a hung loading screen.
    public sealed class XsollaIdAuth : IXsollaIdAuth
    {
        // Do not hand out a token with less than five minutes left, so it does
        // not expire in flight. Same margin as the Firebase anonymous auth
        // provider.
        private const long TokenRefreshMarginMs = 300_000;

        // Five minutes rather than three: the first sign-in often goes through
        // MFA, and the player needs time. Matches the timeout recommended by
        // the Xsolla ID design doc for the same IdP and MFA flow.
        private static readonly TimeSpan PopupTimeout = TimeSpan.FromMinutes(5);

        private readonly XsollaIdConfig _config;
        private readonly IOidcDiscovery _discovery;
        private readonly IXsollaIdBrowser _browser;
        private readonly IXsollaIdTokenClient _tokens;
        private readonly IXsollaIdSessionStore _store;
        private readonly Func<long> _nowUnixMs;
        private readonly Func<string> _pageUrl;

        // Refresh and restore are serialized: two concurrent refreshes would
        // burn the refresh token twice.
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        // A semaphore does NOT fit sign-in: it would queue the second call
        // instead of rejecting it, and a double click would open a second
        // window right after the first one closes. So the task itself is kept
        // and handed out again.
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

        // NOT Interactive by default: the player may only be asked where they
        // pressed the button themselves, and an incidental read before the
        // first sign-in must not look like their gesture.
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
                // ALWAYS warm up discovery, even if there is nothing to restore.
                // SignInAsync will then take it from the cache, and awaiting an
                // already completed task continues synchronously — the player's
                // gesture is preserved and the popup is not blocked. A cold
                // cache on click means a blocked popup.
                _ = WarmDiscoveryAsync();

                XsollaIdSession stored = _store.Load();

                if (stored == null)
                    return XsollaIdResult.Expired();

                if (!stored.IsExpired(_nowUnixMs(), TokenRefreshMarginMs))
                {
                    // Top up the profile exactly once — see TopUpProfileAsync.
                    // Awaiting here is fine: the bootstrap calls RestoreAsync
                    // and discards the task, so the extra round-trip delays
                    // nothing.
                    XsollaIdSession adopted = stored.ProfileFetched ? stored : await TopUpProfileAsync(stored);

                    // Adopt receives the ALREADY topped-up session, so SignedIn
                    // carries the picture right away and subscribers need no
                    // second event.
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

            // A repeated click must not open a second window.
            if (_signInInFlight != null && !_signInInFlight.IsCompleted)
                return _signInInFlight;

            _signInInFlight = SignInInternalAsync();

            return _signInInFlight;
        }

        public async Task SignOutAsync()
        {
            XsollaIdSession session = Current;

            // The local part goes FIRST and unconditionally: even if revocation
            // fails, the player must end up signed out.
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
                // Revocation is best-effort: this Xsolla ID endpoint has
                // returned 405 before, and signing out must not depend on it.
                Debug.LogWarning($"{nameof(XsollaIdAuth)}: token revocation failed — {ex.Message}");
            }
        }

        private async Task<XsollaIdResult> SignInInternalAsync()
        {
            try
            {
                XsollaIdClientRegistration client = _config.Active;

                // Checked BEFORE opening the window: the server compares
                // redirect_uri character by character, and its rejection is
                // vague. This also catches localhost used instead of 127.0.0.1 —
                // they are different origins.
                if (!OriginGuard.IsAllowed(_pageUrl(), _config.AllowedGameOrigins, out string problem))
                    return Fail(XsollaIdErrorCode.NotConfigured, problem);

                OidcConfiguration oidc = await _discovery.GetAsync();

                if (oidc == null || !oidc.IsUsable)
                    return Fail(XsollaIdErrorCode.NotConfigured, "discovery did not return the authorize/token endpoints.");

                // Log exactly what is about to go to the server, not "the
                // address from the config": the server compares redirect_uri
                // character by character, and a mismatch in scheme, port or
                // trailing slash is impossible to spot by looking at the asset.
                // The same address is later used in the code exchange — if it
                // does not match, the rejection comes from there.
                Debug.Log($"{nameof(XsollaIdAuth)}: opening sign-in — page '{_pageUrl()}', " +
                    $"redirect_uri '{client.RedirectUri}', authorize endpoint '{oidc.AuthorizationEndpoint}'.");

                string verifier = Pkce.CreateVerifier();
                string state = Pkce.CreateState();
                string url = BuildAuthorizeUrl(
                    oidc, client, _config.Scopes, Pkce.CreateChallengeS256(verifier), state);

                BrowserAuthResult browser = await _browser.AuthorizeAsync(url, state, PopupTimeout);

                if (!browser.IsSuccess)
                    return new XsollaIdResult.Failure(browser.Error.Value, browser.Message);

                // Compare state BEFORE the exchange: a mismatch may be CSRF, and
                // the one-time code must not be spent on it.
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

        // Called only under _refreshLock.
        // origin is passed through because a token refresh comes from two
        // places: the session restore at startup (a "signed out → signed in"
        // transition, where the player must not be asked anything) and an
        // explicit RefreshAsync for an already signed-in player.
        private async Task<XsollaIdResult> RefreshLockedAsync(
            XsollaIdSession session, XsollaIdSignInOrigin origin)
        {
            TokenResponse tokens = await _tokens.RefreshAsync(session.Refresh.Value);

            if (!tokens.IsSuccess)
            {
                // The refresh token lives 720 hours. Its expiry is a normal
                // outcome, not a failure: clear it and quietly fall back to
                // guest.
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

        // The profile is fetched only if it has not been fetched for this
        // identity yet: userinfo is an extra round-trip, and a token refresh
        // does not change the identity.
        //
        // A non-empty name cannot serve as the marker: the picture claim may be
        // missing entirely, and "no picture → fetch again" would mean an extra
        // userinfo on EVERY token refresh, forever. Hence a dedicated flag that
        // is persisted with the session.
        private async Task<XsollaIdSession> BuildSessionAsync(TokenResponse tokens, XsollaIdSession previous)
        {
            XsollaUserId subject = previous?.Subject ?? XsollaUserId.None;
            string displayName = previous?.DisplayName;
            AvatarUrl picture = previous?.Picture ?? AvatarUrl.None;
            bool profileFetched = previous?.ProfileFetched ?? false;

            // Carried over from the previous session for the same reason as the
            // name and picture: a token refresh does not call userinfo, and the
            // identity is the same.
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

                    // Set ONLY on success: a failed userinfo must be retried on
                    // the next refresh, not remembered as "already asked".
                    profileFetched = true;
                }
                else
                {
                    // Sign-in still succeeded without userinfo — there is just
                    // nothing to display.
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

        // Catch-up profile request for a session loaded from storage.
        //
        // Needed because RestoreAsync adopts a still-valid session WITHOUT going
        // through BuildSessionAsync: a blob written by an older version has a
        // name but no picture, and waiting for a token refresh (where userinfo
        // happens anyway) may take too long — the access token lives an hour,
        // but the player may keep the tab open for days.
        //
        // Never throws and never fails the restore: the session is fully usable
        // without a picture, so any failure is just a warning.
        private async Task<XsollaIdSession> TopUpProfileAsync(XsollaIdSession session)
        {
            // Safety net: there would be nothing to ask with. Currently
            // unreachable — IsExpired treats a missing access token as expired,
            // so such a session goes to refresh and BuildSessionAsync. But there
            // is no reason to depend on a condition in another file.
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

                // An empty claim does not overwrite what the session already
                // has: a catch-up request is meant to add data, not lose it.
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

            // BEFORE the events: a SignedIn subscriber reads the flag right in
            // its handler.
            LastSignInOrigin = origin;

            // SignedIn fires only on the "signed out → signed in" transition:
            // subscribers rely on exactly that meaning.
            //
            // But on a token refresh Current is replaced with a NEW object, and
            // that cannot go unannounced: a subscriber that cached fields from
            // the SignedIn payload would quietly keep the old picture and name.
            // So a session change without a player change has its own event.
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
