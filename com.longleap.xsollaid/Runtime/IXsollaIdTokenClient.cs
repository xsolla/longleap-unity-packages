using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Транспорт к токен-эндпоинтам. Реализация на UnityWebRequest живёт в
    // сборке под XSOLLA_ID; здесь только контракт, чтобы вся машина состояний
    // тестировалась без сети.
    //
    // Ни один метод не бросает: сбой возвращается кодом в результате.
    public interface IXsollaIdTokenClient
    {
        // Обмен одноразового кода на токены (authorization_code + PKCE).
        Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri);

        // Обновление по refresh token'у.
        Task<TokenResponse> RefreshAsync(string refreshToken);

        // Кто это. Нужен только ради имени в UI — без scope profile/email
        // вернётся один sub.
        Task<UserInfoResponse> GetUserInfoAsync(string accessToken);

        // Отзыв токена. Best-effort: у Xsolla ID этот эндпоинт уже отдавал
        // 405, поэтому выход из аккаунта не должен от него зависеть.
        Task RevokeAsync(string accessToken);
    }

    public readonly struct TokenResponse
    {
        public AccessToken Access { get; }
        public RefreshToken Refresh { get; }
        public long ExpiresAtUnixMs { get; }

        // Сырой id_token, если сервер его прислал. Разбирают снаружи; здесь
        // держим строкой, чтобы не тянуть парсер JWT в контракт.
        public string RawIdToken { get; }

        public XsollaIdErrorCode? Error { get; }
        public string Message { get; }

        public bool IsSuccess => Error == null;

        private TokenResponse(
            AccessToken access, RefreshToken refresh, long expiresAtUnixMs, string rawIdToken,
            XsollaIdErrorCode? error, string message)
        {
            Access = access;
            Refresh = refresh;
            ExpiresAtUnixMs = expiresAtUnixMs;
            RawIdToken = rawIdToken;
            Error = error;
            Message = message;
        }

        public static TokenResponse Ok(AccessToken access, RefreshToken refresh, long expiresAtUnixMs, string rawIdToken = null) =>
            new TokenResponse(access, refresh, expiresAtUnixMs, rawIdToken, null, null);

        public static TokenResponse Failed(XsollaIdErrorCode code, string message) =>
            new TokenResponse(AccessToken.None, RefreshToken.None, 0, null, code, message);
    }

    public readonly struct UserInfoResponse
    {
        public XsollaUserId Subject { get; }

        // username, preferred_username или email — что нашлось.
        public string DisplayName { get; }

        // Клеймы username и name как есть, без выбора.
        //
        // Рядом с DisplayName это НЕ дублирование: тот выбран из четырёх клеймов
        // по приоритету (XsollaIdTokenClient.PickDisplayName) и годится там, где
        // место только под одну строку. А раскрытая карточка аккаунта показывает
        // именно две разные величины, и подменять одну другой нельзя: у аккаунта
        // без своего username сервер выдаёт сгенерированный id<цифры>, и он не
        // имеет ничего общего с именем из профиля.
        //
        // Оба могут быть null: клейма может не быть в ответе вовсе.
        public string UserName { get; }
        public string FullName { get; }

        // Адрес картинки профиля. None — штатно: клейм picture необязателен, и
        // отдаёт ли его конкретный инстанс Xsolla ID, заранее неизвестно.
        public AvatarUrl Picture { get; }

        public XsollaIdErrorCode? Error { get; }
        public string Message { get; }

        public bool IsSuccess => Error == null;

        private UserInfoResponse(
            XsollaUserId subject, string displayName, AvatarUrl picture,
            string userName, string fullName,
            XsollaIdErrorCode? error, string message)
        {
            Subject = subject;
            DisplayName = displayName;
            Picture = picture;
            UserName = userName;
            FullName = fullName;
            Error = error;
            Message = message;
        }

        // picture — параметр со значением по умолчанию, а не перегрузка: ровно
        // как rawIdToken у TokenResponse.Ok выше. Пишем `= default`, а не
        // `= AvatarUrl.None`: None это свойство, а значение по умолчанию обязано
        // быть константой времени компиляции.
        //
        // userName и fullName тоже в хвосте и тоже со значениями по умолчанию:
        // вызывающих у Ok немного, но ни один из них не должен ломаться из-за
        // двух полей, нужных одной раскрытой карточке.
        public static UserInfoResponse Ok(
            XsollaUserId subject, string displayName, AvatarUrl picture = default,
            string userName = null, string fullName = null) =>
            new UserInfoResponse(subject, displayName, picture, userName, fullName, null, null);

        public static UserInfoResponse Failed(XsollaIdErrorCode code, string message) =>
            new UserInfoResponse(XsollaUserId.None, null, AvatarUrl.None, null, null, code, message);
    }
}
