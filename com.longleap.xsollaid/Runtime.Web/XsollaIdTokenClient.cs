using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LongLeap.XsollaId.Web
{
    // Обмен кода на токены, обновление, userinfo и отзыв.
    //
    // Ничего не бросает: сбои возвращаются кодом XsollaIdErrorCode, потому что
    // выше эти задачи опрашивает корутина бутстрапа, а исключение в ней — это
    // повисшая загрузка.
    public sealed class XsollaIdTokenClient : IXsollaIdTokenClient
    {
        private readonly XsollaIdConfig _config;
        private readonly IOidcDiscovery _discovery;
        private readonly XsollaIdHttp _http;
        private readonly Func<long> _nowUnixMs;

        public XsollaIdTokenClient(
            XsollaIdConfig config,
            IOidcDiscovery discovery,
            XsollaIdHttp http = null,
            Func<long> nowUnixMs = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _http = http ?? new XsollaIdHttp();
            _nowUnixMs = nowUnixMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public async Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri)
        {
            OidcConfiguration oidc = await _discovery.GetAsync();

            if (oidc == null)
                return TokenResponse.Failed(XsollaIdErrorCode.NotConfigured, "discovery unavailable.");

            // Публичный клиент: секрета нет, подлинность доказывает
            // code_verifier. client_id уезжает в теле.
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = _config.Active.ClientId
            };

            HttpReply reply = await _http.PostFormAsync(oidc.TokenEndpoint, form);

            return ReadTokens(reply, "code exchange");
        }

        public async Task<TokenResponse> RefreshAsync(string refreshToken)
        {
            OidcConfiguration oidc = await _discovery.GetAsync();

            if (oidc == null)
                return TokenResponse.Failed(XsollaIdErrorCode.NotConfigured, "discovery unavailable.");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _config.Active.ClientId
            };

            HttpReply reply = await _http.PostFormAsync(oidc.TokenEndpoint, form);

            return ReadTokens(reply, "token refresh");
        }

        public async Task<UserInfoResponse> GetUserInfoAsync(string accessToken)
        {
            OidcConfiguration oidc = await _discovery.GetAsync();

            if (oidc == null || string.IsNullOrEmpty(oidc.UserInfoEndpoint))
                return UserInfoResponse.Failed(XsollaIdErrorCode.NotConfigured, "userinfo is not declared in discovery.");

            HttpReply reply = await _http.GetAsync(oidc.UserInfoEndpoint, accessToken);

            if (!reply.IsSuccess)
            {
                return UserInfoResponse.Failed(
                    Classify(reply), Describe(reply, "userinfo"));
            }

            try
            {
                JObject o = JObject.Parse(reply.Body);

#if XSOLLA_ID_DEBUG
                // Снимок для отладочной панели: сама она до этого JSON не
                // достаёт, а наружу из userinfo уезжает только выбранное имя.
                // Снимаем ДО проверки sub — ответ без sub тоже надо разглядеть.
                XsollaIdDebugClaims.Capture(o);
#endif

                string sub = ReadStringClaim(o, "sub");

                // sub обязателен по OIDC Core. Нет его — ответ не тот, за
                // который себя выдаёт.
                if (string.IsNullOrEmpty(sub))
                {
                    return UserInfoResponse.Failed(
                        XsollaIdErrorCode.TokenExchangeFailed, "userinfo returned a response without sub.");
                }

                string rawPicture = PickPictureUrl(o);
                AvatarUrl picture = AvatarUrl.TryParse(rawPicture);

                // Клейм пришёл, но непригоден: либо схема не https, либо там
                // вообще не адрес. Вход это не ломает, но заметить стоит. Сам
                // адрес в лог не печатаем — он может нести токен CDN.
                if (!picture.IsValid && !string.IsNullOrEmpty(rawPicture))
                    XsollaIdHttp.LogWarning("userinfo returned a picture claim that is not an absolute https url.");

                return UserInfoResponse.Ok(
                    new XsollaUserId(sub),
                    PickDisplayName(o),
                    picture,

                    // Те же два клейма, что и в PickDisplayName, но БЕЗ выбора:
                    // раскрытой карточке нужны обе величины по отдельности.
                    ReadStringClaim(o, "username"),
                    ReadStringClaim(o, "name"));
            }
            catch (Exception ex)
            {
                return UserInfoResponse.Failed(
                    XsollaIdErrorCode.TokenExchangeFailed, $"userinfo could not be parsed: {ex.Message}");
            }
        }

        public async Task RevokeAsync(string accessToken)
        {
            OidcConfiguration oidc = await _discovery.GetAsync();

            if (oidc == null || string.IsNullOrEmpty(oidc.RevocationEndpoint))
                return;

            var form = new Dictionary<string, string>
            {
                ["token"] = accessToken,
                ["client_id"] = _config.Active.ClientId
            };

            HttpReply reply = await _http.PostFormAsync(oidc.RevocationEndpoint, form);

            if (reply.IsSuccess)
                return;

            // Известная беда: discovery объявляет revocation_endpoint в корне
            // (/oauth2/revoke), а обработчик живёт под /api/oauth2/ — и корень
            // отдаёт 405. Пробуем второй путь ровно один раз, вместо того
            // чтобы навсегда терять отзыв токена.
            //
            // Транспортную ошибку сюда пускаем ОБЯЗАТЕЛЬНО. В браузере корень
            // режется ещё и CORS'ом: на /oauth2/revoke нет
            // Access-Control-Allow-Origin, поэтому ответ (вместе с его 405) до
            // нас не доходит вовсе — UnityWebRequest видит ConnectionError, а
            // StatusCode остаётся нулём. Проверка только на 404/405 работала на
            // desktop и никогда не срабатывала на WebGL: отзыв тихо терялся.
            // У /api/oauth2/revoke CORS настроен, так что второй путь спасает.
            string alternate = SwapApiPrefix(oidc.RevocationEndpoint);

            if (alternate != null &&
                (reply.IsTransportError || reply.StatusCode == 404 || reply.StatusCode == 405))
            {
                HttpReply second = await _http.PostFormAsync(alternate, form);

                if (second.IsSuccess)
                    return;

                XsollaIdHttp.LogWarning(
                    $"token revocation failed at both {XsollaIdHttp.Sanitize(oidc.RevocationEndpoint)} " +
                    $"and {XsollaIdHttp.Sanitize(alternate)}.");
                return;
            }

            XsollaIdHttp.LogWarning(Describe(reply, "token revocation"));
        }

        // /oauth2/... ↔ /api/oauth2/... Возвращает null, если ни то, ни другое.
        public static string SwapApiPrefix(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (url.Contains("/api/oauth2/"))
                return url.Replace("/api/oauth2/", "/oauth2/");

            if (url.Contains("/oauth2/"))
                return url.Replace("/oauth2/", "/api/oauth2/");

            return null;
        }

        private TokenResponse ReadTokens(HttpReply reply, string what)
        {
            if (!reply.IsSuccess)
                return TokenResponse.Failed(Classify(reply), Describe(reply, what));

            try
            {
                JObject o = JObject.Parse(reply.Body);

                string access = (string)o["access_token"];

                if (string.IsNullOrEmpty(access))
                {
                    return TokenResponse.Failed(
                        XsollaIdErrorCode.TokenExchangeFailed, $"{what}: no access_token in the response.");
                }

                string refresh = (string)o["refresh_token"];

                // expires_in — секунды от «сейчас». Переводим в абсолютное
                // время сразу: относительное значение бессмысленно после
                // сохранения на диск.
                int expiresIn = (int?)o["expires_in"] ?? 0;
                long expiresAt = _nowUnixMs() + Math.Max(0, expiresIn) * 1000L;

                return TokenResponse.Ok(
                    new AccessToken(access),
                    string.IsNullOrEmpty(refresh) ? RefreshToken.None : new RefreshToken(refresh),
                    expiresAt,
                    (string)o["id_token"]);
            }
            catch (Exception ex)
            {
                return TokenResponse.Failed(
                    XsollaIdErrorCode.TokenExchangeFailed, $"{what}: response could not be parsed — {ex.Message}");
            }
        }

        // Отдельно транспорт, отдельно отказ сервера: UI на них реагирует
        // по-разному, а истёкший refresh token — вообще штатный исход.
        private static XsollaIdErrorCode Classify(HttpReply reply)
        {
            if (reply.IsTransportError)
                return XsollaIdErrorCode.Network;

            // 400 invalid_grant на refresh_token означает «сессии больше нет».
            if (reply.StatusCode == 400 && Contains(reply.Body, "invalid_grant"))
                return XsollaIdErrorCode.SessionExpired;

            if (reply.StatusCode == 401)
                return XsollaIdErrorCode.SessionExpired;

            return XsollaIdErrorCode.TokenExchangeFailed;
        }

        private static bool Contains(string body, string needle) =>
            !string.IsNullOrEmpty(body) && body.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Describe(HttpReply reply, string what) =>
            reply.IsTransportError
                ? $"{what}: {reply.TransportError}"
                : $"{what}: server responded {reply.StatusCode} ({ShortBody(reply.Body)})";

        // Тело ошибки может содержать описание — но не весь JSON в лог.
        private static string ShortBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "empty response";

            return body.Length <= 200 ? body : body.Substring(0, 200) + "…";
        }

        // Клейм читаем строго по типу.
        //
        // (string)o[key] на клейме, который приехал объектом или массивом,
        // бросает InvalidCastException — а его ловит общий catch в
        // GetUserInfoAsync и превращает ВЕСЬ userinfo в отказ. Из-за одного
        // мусорного поля игрок остался бы без имени, хотя sub на месте.
        private static string ReadStringClaim(JObject o, string key) =>
            o[key] is JValue value && value.Type == JTokenType.String ? (string)value : null;

        // Имя для UI: первый непустой из клеймов.
        //
        // preferred_username в claims_supported у Xsolla ID НЕТ — оставлен на
        // случай другого инстанса, стоит дёшево. email идёт последним: это
        // запасной вариант, и показывать его целиком нельзя (см.
        // DisplayNameFormat).
        private static string PickDisplayName(JObject o)
        {
            foreach (string key in new[] { "username", "preferred_username", "name", "email" })
            {
                string value = ReadStringClaim(o, key);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }

        // Адрес картинки профиля.
        //
        // picture — стандартный клейм OIDC Core, входит в scope profile, который
        // мы и так запрашиваем (XsollaIdConfig.DefaultScopes), и объявлен в
        // claims_supported у прода. Остальные три имени — подстраховка на другие
        // инстансы, стоят дёшево.
        //
        // avatar без суффикса идёт ПОСЛЕДНИМ намеренно: у части провайдеров под
        // этим именем лежит не адрес, а идентификатор пресета. Проверку схемы
        // такое не пройдёт, но пусть сначала попробуются honest-адреса.
        public static string PickPictureUrl(JObject o)
        {
            foreach (string key in new[] { "picture", "picture_url", "avatar_url", "avatar" })
            {
                string value = ReadStringClaim(o, key);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }
    }
}
