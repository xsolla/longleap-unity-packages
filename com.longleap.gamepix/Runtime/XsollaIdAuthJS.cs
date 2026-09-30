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
    // Вход через Xsolla ID силами GamePix SDK v3 (GamePix.user.*).
    //
    // На площадке входом занимается сама площадка: у неё своё окно, свой
    // refresh и свой кэш токена. Нам остаётся спросить готовый ответ — поэтому
    // здесь нет ни PKCE, ни redirect_uri, ни страницы обратного вызова, ни
    // сохранённой сессии. Токен НЕ пишем никуда (требование SDK): при каждом
    // запуске сессию заново отдаёт getUser().
    //
    // Реализует тот же IXsollaIdAuth, что и собственный вход, поэтому BackPack,
    // переключатель сейвов и виджет аккаунта работают без изменений.
    //
    // Наружу не бросает ничего: любой сбой — XsollaIdResult.Failure.
    public sealed class XsollaIdAuthJS : IXsollaIdAuth
    {
        // getUser отвечает гостю «в течение нескольких секунд». Держим меньше
        // readyTimeoutSeconds виджета (12 с), чтобы ответ «нет» успел раньше,
        // чем виджет сдастся сам.
        private static readonly TimeSpan GetUserTimeout = TimeSpan.FromSeconds(10);

        // Окно входа: игрок может проходить MFA — столько же ждёт XsollaIdAuth.
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

        // Окно входа, пока SDK не ответил ни разу. Так бывает вне плеера
        // GamePix (localhost, прямая ссылка): SDK молчит на всё, и ждать пять
        // минут значило бы держать кнопку в «Signing in» без надежды. На
        // площадке getUser отвечает на старте, так что туда это не попадает.
        private static readonly TimeSpan UnconfirmedLoginTimeout = TimeSpan.FromSeconds(20);

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int GpxXsollaIdIsSupported();
        [DllImport("__Internal")] private static extern void GpxXsollaIdCall(int requestId, int login);
        [DllImport("__Internal")] private static extern int GpxXsollaIdSubscribe();
#else
        // В редакторе SDK нет: IsAvailable вернёт false, и до вызовов дело не дойдёт.
        private static int GpxXsollaIdIsSupported() => 0;
        private static void GpxXsollaIdCall(int requestId, int login) =>
            throw new NotSupportedException("XsollaIdAuthJS: GamePix SDK exists only in a WebGL build.");
        private static int GpxXsollaIdSubscribe() => 0;
#endif

        private readonly bool _sdkHasUserApi;

        // SDK есть, но игра открыта не внутри плеера GamePix (localhost,
        // прямая ссылка) — он честно отвечает 404. Входа тогда нет, и кнопку
        // надо спрятать, а не показывать неработающую.
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

            // Приёмник переживает перезагрузку сцены, а этот объект — нет:
            // обработчик ПОДМЕНЯЕМ, а не добавляем, чтобы старый экземпляр не
            // продолжал получать события.
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

        // У SDK нет выхода: следующий же getUser() или authChanged вернул бы
        // игрока обратно. Виджет по этому признаку прячет кнопку выхода.
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

        // SDK кэширует токен и сам обновляет его за минуту до истечения, так что
        // «обновить» — это просто спросить заново.
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

            // Повторный клик не должен открывать второе окно.
            if (_signInInFlight != null && !_signInInFlight.IsCompleted)
            {
                Log("sign-in already in progress — reusing the open GamePix login window.");
                return _signInInFlight;
            }

            Log("opening the GamePix login window via GamePix.user.requestLogin().");

            _signInInFlight = CallAsync(login: true, XsollaIdSignInOrigin.Interactive);

            return _signInInFlight;
        }

        // Выхода у площадки нет — забываем локально. CanSignOut == false, так
        // что из UI сюда не попасть; метод есть ради контракта.
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
                            // Гость — штатный исход getUser(), а не сбой.
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
                // Сессия кончилась или игрок вышел на сайте GamePix.
                Forget();
                return;
            }

            XsollaIdSession session = success.Session;

            if (Current != null && Current.Subject != session.Subject)
            {
                // На сайте вошли под другим аккаунтом: для подписчиков это
                // выход прежнего игрока и вход нового, а не обновление токена.
                Log("authChanged brought a different account — signing the previous one out first.");
                Forget();
            }

            // Вход «из ниоткуда» после старта — это игрок сам вошёл на сайте
            // площадки, то есть его жест: сейвы вправе спросить, какой оставить.
            // Для уже вошедшего это просто обновление токена.
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

        // Все строки журнала с одним префиксом: в консоли браузера их ищут
        // фильтром по имени класса. Токены сюда не попадают никогда.
        internal static void Log(string message) => Debug.Log($"{nameof(XsollaIdAuthJS)}: {message}");

        // ---- разбор ответа; публичные ради EditMode-тестов ----

        // login различает смысл 401: у getUser это гость, у requestLogin —
        // игрок закрыл окно.
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

        // Ключ игрока — sub из JWT, тот же, что отдаёт собственный вход
        // Xsolla ID: под ним лежат облачные сейвы и события BackPack. Подпись
        // не проверяем — токен пришёл от SDK площадки, а проверку делает
        // сервер, которому его предъявляют. Если разобрать не вышло, берём
        // user.id: площадка обещает, что он уникален и не меняется.
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

    // Плоский ответ GamePix.user.* в том виде, как его собирает
    // GamePixXsollaId.jslib ($GpxXsollaIdPack).
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

        // Токен не печатаем: его прячет и AccessToken, но payload — сырая строка.
        public override string ToString() =>
            $"GamePixAuthPayload(requestId={RequestId}, success={Success}, code={Code}, guest={Guest}, " +
            $"id={(string.IsNullOrEmpty(Id) ? "none" : "set")}, token={(string.IsNullOrEmpty(Token) ? "none" : "set")}, " +
            $"expiresAt={ExpiresAt}, message=\"{Message}\")";
    }

    // Приёмник SendMessage из GamePixXsollaId.jslib. Живёт в DontDestroyOnLoad:
    // окно входа площадки переживает перезагрузку сцены, и ответ должен дойти.
    internal sealed class GamePixXsollaIdCallbacks : MonoBehaviour
    {
        // Имя объекта, по которому его находит SendMessage. Менять только
        // вместе с GamePixXsollaId.jslib.
        internal const string ReceiverName = "GamePixXsollaIdCallbacks";

        private static GamePixXsollaIdCallbacks _instance;

        private readonly Dictionary<int, TaskCompletionSource<string>> _pending =
            new Dictionary<int, TaskCompletionSource<string>>();

        private int _nextId = 1;

        // Один на всю игру: его ставит последний созданный XsollaIdAuthJS.
        internal Action<string> AuthChangedHandler { get; set; }

        // Ответил ли SDK хоть раз за жизнь страницы. Свой таймаут (408 из
        // CallAsync) ответом не считается — это как раз молчание.
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

        // send вызывается синхронно, до первого await: для requestLogin это
        // условие, иначе браузер заблокирует окно входа.
        internal async Task<string> CallAsync(bool login, TimeSpan timeout, Action<int, bool> send)
        {
            int id = _nextId++;

            // RunContinuationsAsynchronously: продолжение не должно выполняться
            // внутри кадра SendMessage.
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

        // --- вызывается из GamePixXsollaId.jslib, имена не менять ---

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

            // Опоздавший ответ (после таймаута) выдавать уже некому.
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
