// Compiled only with GAMEPIX_JS: the sign-in path for projects whose GamePix
// plugin has no user API (no Assets/Plugins/GamePix/User, no Gpx.User). It
// talks to window.GamePix.user through GamePixXsollaId.jslib instead. Projects
// whose plugin has Gpx.User use XsollaIdAuthGamePix (compiled without
// GAMEPIX_JS) — exactly one of the two exists in a build.
//
// Known limitation: waits use Task.WhenAny + Task.Delay. Task.Delay relies on a
// timer that does not tick in single-threaded WebGL, so in a WebGL player a
// call can stay pending ("Signing in" forever) even after the SDK answered.
// XsollaIdAuthGamePix drives its waits from the player loop and does not have
// this problem.
#if GAMEPIX_JS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.XsollaId;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.GamePix
{
    // Xsolla ID sign-in through GamePix SDK v3 (GamePix.user.*), via a jslib.
    //
    // On the portal, GamePix handles sign-in itself: its own login window, its
    // own refresh, its own token cache. We only ask for the ready answer, so
    // there is no PKCE, no redirect_uri, no callback page and no stored
    // session. The token is NEVER persisted (SDK requirement): every launch
    // gets the session again from getUser().
    //
    // Implements the same IXsollaIdAuth as the standalone sign-in, so BackPack,
    // the save switch and the account widget work unchanged.
    //
    // Never throws: any failure becomes XsollaIdResult.Failure.
    public sealed class XsollaIdAuthJS : IXsollaIdAuth
    {
        // getUser answers a guest "within a few seconds". Kept below the
        // widget's readyTimeoutSeconds (12 s) so our "no" arrives before the
        // widget gives up on its own.
        private static readonly TimeSpan GetUserTimeout = TimeSpan.FromSeconds(10);

        // Login window: the player may be going through MFA — same as XsollaIdAuth.
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

        // Login window while the SDK has not answered anything yet. Happens
        // outside the GamePix Player (localhost, direct link): the SDK stays
        // silent, and waiting five minutes would hold the button in
        // "Signing in" for nothing. Inside the Player getUser answers at
        // startup, so this does not apply there.
        private static readonly TimeSpan UnconfirmedLoginTimeout = TimeSpan.FromSeconds(20);

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int GpxXsollaIdIsSupported();
        [DllImport("__Internal")] private static extern void GpxXsollaIdCall(int requestId, int login);
        [DllImport("__Internal")] private static extern int GpxXsollaIdSubscribe();
#else
        // No SDK in the Editor: IsAvailable returns false, so these are never called.
        private static int GpxXsollaIdIsSupported() => 0;
        private static void GpxXsollaIdCall(int requestId, int login) =>
            throw new NotSupportedException("XsollaIdAuthJS: GamePix SDK exists only in a WebGL build.");
        private static int GpxXsollaIdSubscribe() => 0;
#endif

        private readonly bool _sdkHasUserApi;

        // The SDK is there, but the game is not running inside the GamePix
        // Player (localhost, direct link) — it answers 404. There is no sign-in
        // then, and the button must be hidden rather than shown broken.
        private bool _notInPlayer;

        private Task<XsollaIdResult> _signInInFlight;

        public XsollaIdAuthJS()
        {
            _sdkHasUserApi = GpxXsollaIdIsSupported() == 1;

            if (!_sdkHasUserApi)
            {
                Log("GamePix user API not found (window.GamePix.user.getUser is missing) — " +
                    "Xsolla ID sign-in unavailable, the button stays hidden.");
                return;
            }

            // The receiver survives a scene reload, this object does not: the
            // handler is REPLACED, not added, so the previous instance stops
            // receiving events.
            GamePixXsollaIdCallbacks callbacks = GamePixXsollaIdCallbacks.Instance;
            callbacks.AuthChangedHandler = OnAuthChanged;

            bool subscribed = GpxXsollaIdSubscribe() == 1;

            Log("GamePix user API found. authChanged subscription: " +
                (subscribed ? "on" : "OFF — GamePix.events.on is missing, sign-in on the site will not reach the game") +
                $". SDK has answered on this page before: {callbacks.HasAnswered}.");
        }

        public bool IsAvailable => _sdkHasUserApi && !_notInPlayer;

        public bool IsReady { get; private set; }

        public bool IsSignedIn => Current != null;

        public XsollaIdSignInOrigin LastSignInOrigin { get; private set; } = XsollaIdSignInOrigin.Restored;

        public XsollaIdSession Current { get; private set; }

        // The SDK has no sign-out: the very next getUser() or authChanged would
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

                Log("restoring the session via GamePix.user.getUser().");

                return await CallAsync(login: false, XsollaIdSignInOrigin.Restored);
            }
            finally
            {
                IsReady = true;
                Log($"restore finished — signed in: {IsSignedIn}, available: {IsAvailable}.");
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

            Log("refreshing the session via GamePix.user.getUser().");

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

            Log("opening the GamePix login window via GamePix.user.requestLogin().");

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

        private async Task<XsollaIdResult> CallAsync(bool login, XsollaIdSignInOrigin origin)
        {
            try
            {
                GamePixXsollaIdCallbacks callbacks = GamePixXsollaIdCallbacks.Instance;

                TimeSpan timeout = !login
                    ? GetUserTimeout
                    : callbacks.HasAnswered ? LoginTimeout : UnconfirmedLoginTimeout;

                if (login && !callbacks.HasAnswered)
                {
                    Log($"the SDK has not answered anything on this page yet — the login window " +
                        $"gets {timeout.TotalSeconds:0} s instead of {LoginTimeout.TotalMinutes:0} min.");
                }

                string json = await callbacks.CallAsync(
                    login, timeout,
                    (id, isLogin) => GpxXsollaIdCall(id, isLogin ? 1 : 0));

                GamePixAuthPayload payload = GamePixAuthPayload.Parse(json);
                XsollaIdResult result = ToResult(payload, login);

                Log((login ? "requestLogin" : "getUser") + " → " +
                    (payload?.ToString() ?? "empty answer") + " → " + Describe(result) + ".");

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
                            // A guest is a normal getUser() outcome, not an error.
                            Log("the player is a guest on GamePix (code 401).");
                            Forget();
                        }
                        else if (failure.Code == XsollaIdErrorCode.Cancelled)
                        {
                            Log("the player closed the GamePix login window (code 401).");
                        }
                        else
                        {
                            Debug.LogWarning($"{nameof(XsollaIdAuthJS)}: {failure.Code} — {failure.Message}");
                        }

                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaIdAuthJS)}: GamePix user call failed — {ex.Message}");
                return new XsollaIdResult.Failure(XsollaIdErrorCode.Network, $"GamePix user call failed: {ex.Message}");
            }
        }

        private void OnAuthChanged(string json)
        {
            XsollaIdResult result;

            GamePixAuthPayload payload;

            try
            {
                payload = GamePixAuthPayload.Parse(json);
                result = ToResult(payload, login: false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaIdAuthJS)}: authChanged payload could not be parsed — {ex.Message}");
                return;
            }

            Log("authChanged → " + (payload?.ToString() ?? "empty answer") + " → " + Describe(result) + ".");

            if (!(result is XsollaIdResult.Success success))
            {
                // The session ended or the player signed out on the GamePix site.
                Forget();
                return;
            }

            XsollaIdSession session = success.Session;

            if (Current != null && Current.Subject != session.Subject)
            {
                // A different account signed in on the site: for subscribers this
                // is the previous player signing out and a new one signing in,
                // not a token update.
                Log("authChanged brought a different account — signing the previous one out first.");
                Forget();
            }

            // A sign-in "out of nowhere" after startup means the player signed in
            // on the portal site — their own gesture, so the save switch may ask
            // which save to keep. For an already signed-in player it is just a
            // token update.
            XsollaIdSignInOrigin origin = Current == null && IsReady
                ? XsollaIdSignInOrigin.Interactive
                : XsollaIdSignInOrigin.Refreshed;

            Adopt(session, origin);
        }

        private void Adopt(XsollaIdSession session, XsollaIdSignInOrigin origin)
        {
            bool wasSignedOut = Current == null;

            Current = session;
            LastSignInOrigin = origin;

            Log(wasSignedOut
                ? $"signed in ({origin}) — name set: {!string.IsNullOrEmpty(session.DisplayName)}."
                : $"session updated ({origin}).");

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
            !_sdkHasUserApi ? "GamePix user API not found" : "not running inside the GamePix Player";

        private static string Describe(XsollaIdResult result) =>
            result is XsollaIdResult.Failure failure ? $"failure {failure.Code}" : "success";

        // Every log line carries the same prefix: filter the browser console by
        // the class name. Tokens never get here.
        internal static void Log(string message) => Debug.Log($"{nameof(XsollaIdAuthJS)}: {message}");

        // ---- answer mapping; public for the EditMode tests ----

        // login tells the two meanings of 401 apart: for getUser it is a guest,
        // for requestLogin the player closed the window.
        public static XsollaIdResult ToResult(GamePixAuthPayload payload, bool login)
        {
            if (payload == null)
                return new XsollaIdResult.Failure(XsollaIdErrorCode.Network, "GamePix returned an empty answer.");

            if (payload.Success && payload.Code == 200)
            {
                XsollaUserId subject = ReadSubject(payload.Token, payload.Id);

                if (!subject.IsValid)
                {
                    return new XsollaIdResult.Failure(XsollaIdErrorCode.TokenExchangeFailed,
                        "GamePix signed the player in but returned no user id.");
                }

                string displayName = FirstNonEmpty(payload.Nickname, payload.Username, payload.Name);

                var session = new XsollaIdSession(
                    subject,
                    displayName,
                    string.IsNullOrEmpty(payload.Token) ? AccessToken.None : new AccessToken(payload.Token),
                    RefreshToken.None,
                    payload.ExpiresAt,
                    AvatarUrl.TryParse(payload.Avatar),
                    profileFetched: true,
                    userName: NullIfEmpty(payload.Username),
                    fullName: NullIfEmpty(payload.Name));

                return new XsollaIdResult.Success(session);
            }

            switch (payload.Code)
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
                        $"GamePix answered with code {payload.Code}: {payload.Message}");
            }
        }

        // The player key is the JWT sub — the same one the standalone Xsolla ID
        // sign-in yields: cloud saves and BackPack events live under it. The
        // signature is not checked: the token came from the portal SDK, and the
        // server it is presented to verifies it. If it cannot be read, fall
        // back to user.id: the portal promises it is unique and stable.
        public static XsollaUserId ReadSubject(string token, string fallbackId)
        {
            string sub = TryReadJwtSubject(token);

            if (!string.IsNullOrEmpty(sub))
                return new XsollaUserId(sub);

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

    // The flat GamePix.user.* answer as built by GamePixXsollaId.jslib
    // ($GpxXsollaIdPack).
    public sealed class GamePixAuthPayload
    {
        [JsonProperty("requestId")] public int RequestId { get; set; }
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("code")] public int Code { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("nickname")] public string Nickname { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("avatar")] public string Avatar { get; set; }
        [JsonProperty("guest")] public bool Guest { get; set; }
        [JsonProperty("token")] public string Token { get; set; }
        [JsonProperty("expiresAt")] public long ExpiresAt { get; set; }

        public static GamePixAuthPayload Parse(string json) =>
            string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<GamePixAuthPayload>(json);

        // Never prints the token: AccessToken hides it too, but the payload is a raw string.
        public override string ToString() =>
            $"GamePixAuthPayload(requestId={RequestId}, success={Success}, code={Code}, guest={Guest}, " +
            $"id={(string.IsNullOrEmpty(Id) ? "none" : "set")}, token={(string.IsNullOrEmpty(Token) ? "none" : "set")}, " +
            $"expiresAt={ExpiresAt}, message=\"{Message}\")";
    }

    // SendMessage receiver for GamePixXsollaId.jslib. Lives in DontDestroyOnLoad:
    // the portal login window survives a scene reload, and the answer must arrive.
    internal sealed class GamePixXsollaIdCallbacks : MonoBehaviour
    {
        // The object name SendMessage finds it by. Change only together with
        // GamePixXsollaId.jslib.
        internal const string ReceiverName = "GamePixXsollaIdCallbacks";

        private static GamePixXsollaIdCallbacks _instance;

        private readonly Dictionary<int, TaskCompletionSource<string>> _pending =
            new Dictionary<int, TaskCompletionSource<string>>();

        private int _nextId = 1;

        // One per game: set by the most recently created XsollaIdAuthJS.
        internal Action<string> AuthChangedHandler { get; set; }

        // Whether the SDK has answered anything at all on this page. Our own
        // timeout (the 408 from CallAsync) does not count — that is exactly
        // the silence.
        internal bool HasAnswered { get; private set; }

        internal static GamePixXsollaIdCallbacks Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new GameObject(ReceiverName).AddComponent<GamePixXsollaIdCallbacks>();

                return _instance;
            }
        }

        private void Awake() => DontDestroyOnLoad(gameObject);

        // send is called synchronously, before the first await: requestLogin
        // requires it, otherwise the browser blocks the login window.
        internal async Task<string> CallAsync(bool login, TimeSpan timeout, Action<int, bool> send)
        {
            int id = _nextId++;

            // RunContinuationsAsynchronously: the continuation must not run
            // inside the SendMessage frame.
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            _pending[id] = completion;

            using (var cancel = new CancellationTokenSource())
            {
                string method = login ? "requestLogin" : "getUser";
                var clock = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    XsollaIdAuthJS.Log($"request #{id}: {method}() sent, timeout {timeout.TotalSeconds:0} s.");

                    send(id, login);

                    Task delay = Task.Delay(timeout, cancel.Token);
                    Task first = await Task.WhenAny(completion.Task, delay);

                    if (first != completion.Task)
                    {
                        Debug.LogWarning(
                            $"{nameof(XsollaIdAuthJS)}: request #{id}: {method}() got no answer in " +
                            $"{timeout.TotalSeconds:0} s — treating it as a timeout (408). " +
                            $"SDK has answered on this page before: {HasAnswered}.");
                        return "{\"requestId\":" + id + ",\"success\":false,\"code\":408}";
                    }

                    XsollaIdAuthJS.Log($"request #{id}: {method}() answered in {clock.ElapsedMilliseconds} ms.");

                    return await completion.Task;
                }
                finally
                {
                    cancel.Cancel();
                    _pending.Remove(id);
                }
            }
        }

        // --- called from GamePixXsollaId.jslib, do not rename ---

        public void OnAuthResult(string json)
        {
            int id;

            try
            {
                id = (int?)JObject.Parse(json)["requestId"] ?? 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(XsollaIdAuthJS)}: GamePix answer could not be parsed — {ex.Message}");
                return;
            }

            HasAnswered = true;

            // A late answer (after the timeout) has no one to go to.
            if (_pending.TryGetValue(id, out TaskCompletionSource<string> completion))
                completion.TrySetResult(json);
            else
                XsollaIdAuthJS.Log($"request #{id}: answer arrived after its timeout — ignored.");
        }

        public void OnAuthChanged(string json)
        {
            HasAnswered = true;

            if (AuthChangedHandler == null)
                XsollaIdAuthJS.Log("authChanged arrived but no XsollaIdAuthJS listens — ignored.");
            AuthChangedHandler?.Invoke(json);
        }
    }
}
#endif
