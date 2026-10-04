using System;
using System.Text;
using System.Threading.Tasks;
using GamePix;
using GamePix.User;
using LongLeap.XsollaId;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.GamePix
{
    // Xsolla ID sign-in through the GamePix plugin's own user API (Gpx.User).
    //
    // On the portal, GamePix handles sign-in itself: its own login window, its
    // own token refresh, its own token cache. We only ask for the ready answer,
    // so there is no PKCE, no redirect_uri, no callback page and no stored
    // session. The token is NEVER persisted or logged (SDK requirement): every
    // launch gets the session again from GetUser().
    //
    // Implements the same IXsollaIdAuth as the standalone sign-in, so BackPack,
    // the save switch and the account widget work unchanged.
    //
    // Replaces XsollaIdAuthJS. That class waited with Task.WhenAny + Task.Delay,
    // and both go through the .NET thread pool, which never runs in
    // single-threaded WebGL: the SDK answered, but the game stayed in
    // "Signing in" forever. Here every wait is driven by Unity's player loop
    // (Awaitable.NextFrameAsync + realtime clock) — no TaskCompletionSource
    // continuations, no Task.WhenAny, no Task.Delay.
    //
    // Never throws: any failure becomes XsollaIdResult.Failure.
    public sealed class XsollaIdAuthGamePix : IXsollaIdAuth
    {
        // GetUser answers a guest "within a few seconds". Kept below the
        // widget's readyTimeoutSeconds (12 s) so our "no" arrives before the
        // widget gives up on its own.
        private const double GetUserTimeoutSeconds = 10;

        // Login window: the player may be going through MFA — same as XsollaIdAuth.
        private const double LoginTimeoutSeconds = 5 * 60;

        // Login window while the SDK has not answered anything on this page.
        // Happens outside the GamePix Player (localhost, direct link): the SDK
        // may stay silent, and holding the button in "Signing in" for five
        // minutes would be pointless. Inside the Player GetUser answers at
        // startup, so this does not apply there.
        private const double UnconfirmedLoginTimeoutSeconds = 20;

        // Page-wide state. Statics survive the scene reload after death, the
        // instance does not.
        //
        // GamepixUser.OnAuthChanged registers a NEW JS GamePix.events.on
        // listener on every subscription, so we subscribe exactly once per
        // page (per IUser object) and each new instance only swaps the handler.
        private static IUser s_subscribedTo;
        private static Action<AuthResult> s_authChangedHandler;

        // Whether the SDK has answered anything at all on this page. Our own
        // timeout does not count — that is exactly the silence.
        private static bool s_sdkHasAnswered;

        private static int s_nextRequestId = 1;

        private readonly IUser _user;

        // The SDK answered 404: the game is not running inside the GamePix
        // Player (localhost, direct link). There is no sign-in then, and the
        // button must be hidden rather than shown broken.
        private bool _notInPlayer;

        private Task<XsollaIdResult> _signInInFlight;

        public XsollaIdAuthGamePix() : this(Gpx.User)
        {
        }

        public XsollaIdAuthGamePix(IUser user)
        {
            _user = user;

            if (_user == null)
            {
                Log("Gpx.User is null — Xsolla ID sign-in unavailable, the button stays hidden.");
                return;
            }

            bool replaced = s_authChangedHandler != null;
            s_authChangedHandler = OnAuthChanged;

            string subscription;

            if (ReferenceEquals(s_subscribedTo, _user))
            {
                subscription = "reused from a previous scene";
            }
            else
            {
                try
                {
                    _user.OnAuthChanged += ForwardAuthChanged;
                    s_subscribedTo = _user;
                    subscription = "subscribed";
                }
                catch (Exception ex)
                {
                    subscription = $"FAILED ({ex.Message}) — sign-in on the GamePix site will not reach the game";
                    Debug.LogWarning($"{nameof(XsollaIdAuthGamePix)}: authChanged subscription failed — {ex.Message}");
                }
            }

            Log($"created. SDK user API: {_user.GetType().Name}. authChanged: {subscription}" +
                (replaced ? ", handler of the previous instance replaced" : "") +
                $". SDK has answered on this page before: {s_sdkHasAnswered}.");
        }

        public bool IsAvailable => _user != null && !_notInPlayer;

        public bool IsReady { get; private set; }

        public bool IsSignedIn => Current != null;

        public XsollaIdSignInOrigin LastSignInOrigin { get; private set; } = XsollaIdSignInOrigin.Restored;

        public XsollaIdSession Current { get; private set; }

        // The SDK has no sign-out: the very next GetUser() or authChanged would
        // bring the player back. The widget hides the sign-out button on this.
        public bool CanSignOut => false;

        public event Action<XsollaIdSession> SignedIn;
        public event Action SignedOut;
        public event Action<XsollaIdSession> SessionUpdated;

        public async Task<XsollaIdResult> RestoreAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    Log($"restore skipped — {UnavailableReason()}.");
                    return XsollaIdResult.Unavailable();
                }

                Log("restoring the session via Gpx.User.GetUser().");

                return await CallAsync(login: false, XsollaIdSignInOrigin.Restored);
            }
            finally
            {
                IsReady = true;
                Log($"restore finished — signed in: {IsSignedIn}, available: {IsAvailable}, ready: {IsReady}.");
            }
        }

        // The SDK caches the token and renews it a minute before expiry, so
        // "refresh" just means asking again.
        public Task<XsollaIdResult> RefreshAsync()
        {
            if (!IsAvailable)
            {
                Log($"refresh skipped — {UnavailableReason()}.");
                return Task.FromResult(XsollaIdResult.Unavailable());
            }

            Log("refreshing the session via Gpx.User.GetUser().");

            return CallAsync(login: false, XsollaIdSignInOrigin.Refreshed);
        }

        public Task<XsollaIdResult> SignInAsync()
        {
            if (!IsAvailable)
            {
                Log($"sign-in skipped — {UnavailableReason()}.");
                return Task.FromResult(XsollaIdResult.Unavailable());
            }

            // A second click must not open a second window.
            if (_signInInFlight != null && !_signInInFlight.IsCompleted)
            {
                Log("sign-in already in progress — reusing the open GamePix login window.");
                return _signInInFlight;
            }

            Log("opening the GamePix login window via Gpx.User.RequestLogin().");

            _signInInFlight = CallAsync(login: true, XsollaIdSignInOrigin.Interactive);

            return _signInInFlight;
        }

        // The portal has no sign-out — forget locally. CanSignOut == false, so
        // the UI never gets here; the method exists for the contract.
        public Task SignOutAsync()
        {
            Log("sign-out requested — forgetting the session locally (GamePix has no sign-out).");
            Forget();
            return Task.CompletedTask;
        }

        // ---- SDK calls ----

        private sealed class PendingCall
        {
            public int Id;
            public string Method;
            public bool Answered;
            public bool Expired;
            public AuthResult Result;
            public double SentAt;
        }

        private async Task<XsollaIdResult> CallAsync(bool login, XsollaIdSignInOrigin origin)
        {
            try
            {
                var call = new PendingCall
                {
                    Id = s_nextRequestId++,
                    Method = login ? "RequestLogin" : "GetUser",
                    SentAt = Time.realtimeSinceStartupAsDouble,
                };

                double timeout = !login
                    ? GetUserTimeoutSeconds
                    : s_sdkHasAnswered ? LoginTimeoutSeconds : UnconfirmedLoginTimeoutSeconds;

                string why = !login
                    ? "getUser timeout"
                    : s_sdkHasAnswered
                        ? "login timeout, the SDK has answered on this page before"
                        : "short login timeout, the SDK has NOT answered anything on this page yet";

                Log($"request #{call.Id}: {call.Method}() sending, timeout {timeout:0} s ({why}).");

                // The SDK is called synchronously, before the first await: for
                // RequestLogin this is required, otherwise the browser blocks
                // the login window (it must open inside the click gesture).
                //
                // Note: GamepixUser merges overlapping calls — while one
                // GetUser/RequestLogin is pending, later ones attach to it and
                // send nothing new. If an answer is lost, later calls stall
                // inside the SDK too; our timeout only frees our caller.
                Action<AuthResult> onResult = result => OnCallAnswered(call, result);

                if (login)
                    _user.RequestLogin(onResult);
                else
                    _user.GetUser(onResult);

                double deadline = call.SentAt + timeout;

                // Driven by the player loop: works in WebGL and ignores
                // Time.timeScale (GamePix sets it to 0 while the game is paused).
                // The Editor stub answers synchronously — then we never wait.
                while (!call.Answered && Time.realtimeSinceStartupAsDouble < deadline)
                    await Awaitable.NextFrameAsync();

                AuthResult answer;

                if (call.Answered)
                {
                    answer = call.Result;
                    Log($"request #{call.Id}: {call.Method}() caller resumed after " +
                        $"{ElapsedMs(call)} ms.");
                }
                else
                {
                    call.Expired = true;
                    Debug.LogWarning(
                        $"{nameof(XsollaIdAuthGamePix)}: request #{call.Id}: {call.Method}() got no answer in " +
                        $"{timeout:0} s — treating it as a timeout (408). " +
                        $"SDK has answered on this page before: {s_sdkHasAnswered}.");
                    answer = new AuthResult { success = false, code = 408, message = "no answer from the SDK in time (client-side timeout)" };
                }

                XsollaIdResult result = ToResult(answer, login, out string subjectSource);

                Log($"request #{call.Id}: {call.Method}() → {Describe(answer)} → {Describe(result)}" +
                    (subjectSource != null ? $", player id from {subjectSource}" : "") + ".");

                switch (result)
                {
                    case XsollaIdResult.Success success:
                        Adopt(success.Session, origin);
                        break;

                    case XsollaIdResult.Failure failure:
                        if (failure.Code == XsollaIdErrorCode.Unavailable)
                        {
                            _notInPlayer = true;
                            Log("not running inside the GamePix Player (code 404) — sign-in hidden.");
                        }
                        else if (failure.Code == XsollaIdErrorCode.SessionExpired)
                        {
                            // A guest is a normal GetUser outcome, not an error.
                            Log("the player is a guest on GamePix (code 401).");
                            Forget();
                        }
                        else if (failure.Code == XsollaIdErrorCode.Cancelled)
                        {
                            Log("the player closed the GamePix login window (code 401).");
                        }
                        else
                        {
                            Debug.LogWarning($"{nameof(XsollaIdAuthGamePix)}: {failure.Code} — {failure.Message}");
                        }

                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaIdAuthGamePix)}: GamePix user call failed — {ex}");
                return new XsollaIdResult.Failure(XsollaIdErrorCode.Network, $"GamePix user call failed: {ex.Message}");
            }
        }

        // Runs inside the SDK callback (in WebGL — a MonoPInvokeCallback called
        // from JS). Only records the answer; all game logic runs later in the
        // waiting caller, on the player loop.
        private static void OnCallAnswered(PendingCall call, AuthResult result)
        {
            try
            {
                s_sdkHasAnswered = true;

                if (call.Expired)
                {
                    Log($"request #{call.Id}: {call.Method}() answer arrived after its timeout " +
                        $"({ElapsedMs(call)} ms) — ignored. {Describe(result)}.");
                    return;
                }

                if (call.Answered)
                {
                    Log($"request #{call.Id}: {call.Method}() answered twice — the second answer is ignored.");
                    return;
                }

                call.Result = result;
                call.Answered = true;

                Log($"request #{call.Id}: {call.Method}() answer arrived from the SDK in {ElapsedMs(call)} ms.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaIdAuthGamePix)}: SDK answer could not be recorded — {ex.Message}");
            }
        }

        // ---- authChanged ----

        // Static so the single SDK subscription can serve whichever instance is
        // current. Handling is deferred to the next frame: SignedIn / SignedOut
        // subscribers must not run inside the JS callback.
        private static async void ForwardAuthChanged(AuthResult result)
        {
            try
            {
                s_sdkHasAnswered = true;

                Log($"authChanged arrived from the SDK: {Describe(result)}.");

                await Awaitable.NextFrameAsync();

                Action<AuthResult> handler = s_authChangedHandler;

                if (handler == null)
                {
                    Log("authChanged: no XsollaIdAuthGamePix listens — ignored.");
                    return;
                }

                handler(result);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaIdAuthGamePix)}: authChanged handling failed — {ex}");
            }
        }

        private void OnAuthChanged(AuthResult answer)
        {
            XsollaIdResult result = ToResult(answer, login: false, out string subjectSource);

            if (!(result is XsollaIdResult.Success success))
            {
                // The session ended or the player signed out on the GamePix site.
                Log($"authChanged → {Describe(result)} → " +
                    (IsSignedIn ? "sign-out." : "still signed out, nothing to do."));
                Forget();
                return;
            }

            XsollaIdSession session = success.Session;

            if (Current != null && Current.Subject != session.Subject)
            {
                // A different account signed in on the site: for subscribers this
                // is the previous player signing out and a new one signing in,
                // not a token update.
                Log($"authChanged → account switch (player id from {subjectSource}) — " +
                    "signing the previous account out first.");
                Forget();
            }

            // A sign-in "out of nowhere" after startup means the player signed in
            // on the portal site — their own gesture, so the save switch may ask
            // which save to keep. For an already signed-in player it is just a
            // token update.
            XsollaIdSignInOrigin origin = Current == null && IsReady
                ? XsollaIdSignInOrigin.Interactive
                : XsollaIdSignInOrigin.Refreshed;

            Log(Current == null
                ? $"authChanged → new sign-in ({origin}), player id from {subjectSource}."
                : "authChanged → token refresh.");

            Adopt(session, origin);
        }

        // ---- state ----

        private void Adopt(XsollaIdSession session, XsollaIdSignInOrigin origin)
        {
            bool wasSignedOut = Current == null;

            Current = session;
            LastSignInOrigin = origin;

            Log(wasSignedOut
                ? $"signed in ({origin}) — name set: {!string.IsNullOrEmpty(session.DisplayName)}, " +
                  $"avatar set: {session.Picture.IsValid}, expiresAt: {session.ExpiresAtUnixMs}."
                : $"session updated ({origin}), expiresAt: {session.ExpiresAtUnixMs}.");

            if (wasSignedOut)
                SignedIn?.Invoke(session);
            else
                SessionUpdated?.Invoke(session);
        }

        private void Forget()
        {
            if (Current == null)
                return;

            Log("signed out — the session is forgotten.");

            Current = null;
            SignedOut?.Invoke();
        }

        private string UnavailableReason() =>
            _user == null ? "Gpx.User is null" : "not running inside the GamePix Player";

        private static long ElapsedMs(PendingCall call) =>
            (long)((Time.realtimeSinceStartupAsDouble - call.SentAt) * 1000);

        // Every log line carries the same prefix: filter the browser console by
        // the class name. Tokens never get here.
        private static void Log(string message) => Debug.Log($"{nameof(XsollaIdAuthGamePix)}: {message}");

        // Never prints the token itself — only whether it is present.
        private static string Describe(AuthResult answer)
        {
            if (answer == null)
                return "empty answer";

            UserInfo user = answer.user;

            return $"AuthResult(success={answer.success}, code={answer.code}, " +
                   $"guest={(user != null && user.guest)}, id={(string.IsNullOrEmpty(user?.id) ? "none" : "set")}, " +
                   $"token={(string.IsNullOrEmpty(answer.token) ? "none" : "set")}, " +
                   $"expiresAt={(long)answer.expiresAt}, message=\"{answer.message}\")";
        }

        private static string Describe(XsollaIdResult result)
        {
            if (!(result is XsollaIdResult.Failure failure))
                return "signed in";

            switch (failure.Code)
            {
                case XsollaIdErrorCode.SessionExpired: return "guest";
                case XsollaIdErrorCode.Cancelled: return "login window closed";
                case XsollaIdErrorCode.Unavailable: return "not in the GamePix Player";
                case XsollaIdErrorCode.Timeout: return "timeout";
                default: return $"error {failure.Code}";
            }
        }

        // ---- mapping ----

        // login tells the two meanings of 401 apart: for GetUser it is a
        // guest, for RequestLogin the player closed the window.
        private static XsollaIdResult ToResult(AuthResult answer, bool login, out string subjectSource)
        {
            subjectSource = null;

            if (answer == null)
                return new XsollaIdResult.Failure(XsollaIdErrorCode.Network, "GamePix returned an empty answer.");

            if (answer.success && answer.code == 200)
            {
                UserInfo user = answer.user ?? new UserInfo();

                XsollaUserId subject = ReadSubject(answer.token, user.id, out subjectSource);

                if (!subject.IsValid)
                {
                    return new XsollaIdResult.Failure(XsollaIdErrorCode.TokenExchangeFailed,
                        "GamePix signed the player in but returned no user id.");
                }

                var session = new XsollaIdSession(
                    subject,
                    FirstNonEmpty(user.nickname, user.username, user.name),
                    string.IsNullOrEmpty(answer.token) ? AccessToken.None : new AccessToken(answer.token),
                    RefreshToken.None,
                    (long)answer.expiresAt,
                    AvatarUrl.TryParse(user.avatar),
                    profileFetched: true,
                    userName: NullIfEmpty(user.username),
                    fullName: NullIfEmpty(user.name));

                return new XsollaIdResult.Success(session);
            }

            switch (answer.code)
            {
                case 401:
                    return login ? XsollaIdResult.Cancelled() : XsollaIdResult.Expired();

                case 404:
                    return XsollaIdResult.Unavailable();

                case 408:
                    return new XsollaIdResult.Failure(XsollaIdErrorCode.Timeout,
                        "the GamePix Player did not answer in time.");

                default:
                    return new XsollaIdResult.Failure(XsollaIdErrorCode.Network,
                        $"GamePix answered with code {answer.code}: {answer.message}");
            }
        }

        // The player key is the JWT sub — the same one the standalone Xsolla ID
        // sign-in yields: cloud saves and BackPack events live under it. The
        // signature is not checked: the token came from the portal SDK, and the
        // server it is presented to verifies it. If it cannot be read, fall
        // back to user.id: the portal promises it is unique and stable (the
        // Editor stub's fake token always takes this path).
        private static XsollaUserId ReadSubject(string token, string fallbackId, out string source)
        {
            string sub = TryReadJwtSubject(token);

            if (!string.IsNullOrEmpty(sub))
            {
                source = "jwt sub";
                return new XsollaUserId(sub);
            }

            source = "user.id";
            return string.IsNullOrEmpty(fallbackId) ? XsollaUserId.None : new XsollaUserId(fallbackId);
        }

        private static string TryReadJwtSubject(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            string[] parts = token.Split('.');

            if (parts.Length < 2)
                return null;

            try
            {
                string b64 = parts[1].Replace('-', '+').Replace('_', '/');

                switch (b64.Length % 4)
                {
                    case 2: b64 += "=="; break;
                    case 3: b64 += "="; break;
                }

                string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));

                return (string)JObject.Parse(json)["sub"];
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
